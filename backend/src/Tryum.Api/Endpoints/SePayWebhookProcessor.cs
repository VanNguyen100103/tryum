using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tryum.Domain.Sales;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>Kết quả xử lý một webhook giao dịch ngân hàng.</summary>
public enum WebhookOutcome
{
    /// <summary>Khớp được hóa đơn và đã cộng tiền.</summary>
    Handled,

    /// <summary>Giao dịch đã xử lý trước đó (SePay retry) – bỏ qua an toàn.</summary>
    Duplicate,

    /// <summary>Không tìm thấy hóa đơn khớp nội dung chuyển khoản.</summary>
    Unmatched,

    /// <summary>Không cần xử lý (tiền ra, hóa đơn đã hủy/đã thu đủ…).</summary>
    Ignored,

    /// <summary>Payload thiếu trường bắt buộc.</summary>
    Invalid,
}

public sealed record WebhookResult(
    WebhookOutcome Outcome,
    string Message,
    string? OrderCode = null,
    decimal Amount = 0,
    OrderStatus? Status = null);

/// <summary>
/// Xử lý payload webhook SePay: tìm hóa đơn theo mã trong nội dung chuyển khoản,
/// cộng tiền idempotent (UNIQUE provider + event id) và cập nhật trạng thái hóa đơn.
/// Tách khỏi HTTP để endpoint thật và endpoint giả lập dùng chung một logic.
/// </summary>
public static class SePayWebhookProcessor
{
    public const string Provider = "SePay";

    public static async Task<WebhookResult> ProcessAsync(
        ApplicationDbContext db,
        JsonElement payload,
        string rawPayload,
        CancellationToken ct)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return new WebhookResult(WebhookOutcome.Invalid, "Payload không phải JSON object.");

        var eventId = ReadString(payload, "id");
        if (string.IsNullOrWhiteSpace(eventId))
            return new WebhookResult(WebhookOutcome.Invalid, "Payload thiếu id giao dịch.");

        var transferType = ReadString(payload, "transferType");
        if (!string.Equals(transferType, "in", StringComparison.OrdinalIgnoreCase))
            return new WebhookResult(WebhookOutcome.Ignored, "Bỏ qua giao dịch tiền ra.");

        var amount = ReadDecimal(payload, "transferAmount");
        if (amount <= 0)
            return new WebhookResult(WebhookOutcome.Invalid, "Số tiền giao dịch không hợp lệ.");

        var content = ReadString(payload, "content") ?? string.Empty;
        var code = ReadString(payload, "code");
        var referenceCode = ReadString(payload, "referenceCode");

        // QR tại quầy (thanh toán kết hợp) dùng mã POS... độc lập với hóa đơn.
        var posReference = SePayTransactionParser.ExtractPosReference(content)
            ?? SePayTransactionParser.ExtractPosReference(code);
        if (posReference is not null)
            return await ProcessPosQrAsync(db, posReference, eventId, amount, rawPayload, ct);

        var orderCode = SePayTransactionParser.ExtractOrderCode(content)
            ?? SePayTransactionParser.ExtractOrderCode(code);
        if (orderCode is null)
            return new WebhookResult(WebhookOutcome.Unmatched, "Không tìm thấy mã hóa đơn trong nội dung chuyển khoản.");

        // Webhook không có JWT nên không có tenant: tra hóa đơn toàn cục theo mã duy nhất,
        // sau đó mới đặt TenantContext đúng tenant của hóa đơn.
        var header = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(order => order.Code == orderCode)
            .Select(order => new { order.Id, order.TenantId })
            .FirstOrDefaultAsync(ct);
        if (header is null)
            return new WebhookResult(WebhookOutcome.Unmatched, $"Không có hóa đơn {orderCode}.");

        using var tenantScope = TenantContext.Use(header.TenantId);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Chống trùng: SePay có thể retry hoặc replay cùng một giao dịch.
            var alreadyProcessed = await db.PaymentWebhookEvents
                .AnyAsync(item => item.Provider == Provider && item.EventId == eventId, ct);
            if (alreadyProcessed)
            {
                await transaction.RollbackAsync(ct);
                return new WebhookResult(WebhookOutcome.Duplicate, "Giao dịch đã được xử lý trước đó.", orderCode);
            }

            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Provider = Provider,
                EventId = eventId,
                ReceivedAt = DateTimeOffset.UtcNow,
                PayloadJson = rawPayload,
            });

            var order = await db.Orders
                .Include(row => row.Payments)
                .FirstOrDefaultAsync(row => row.Id == header.Id, ct);
            if (order is null)
            {
                await transaction.RollbackAsync(ct);
                return new WebhookResult(WebhookOutcome.Unmatched, $"Không đọc được hóa đơn {orderCode}.", orderCode);
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new WebhookResult(WebhookOutcome.Ignored, "Hóa đơn đã hủy, không cộng tiền.", orderCode, amount, order.Status);
            }

            if (order.PaidAmount >= order.Total && order.Total > 0)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new WebhookResult(WebhookOutcome.Ignored, "Hóa đơn đã thu đủ tiền.", orderCode, amount, order.Status);
            }

            // Ưu tiên xác nhận khoản "Chuyển khoản" đang chờ; nếu chuyển nhiều hơn thì ghi thêm.
            var pending = order.Payments
                .Where(payment => payment.Method == PaymentMethod.Transfer && payment.Status == PaymentStatus.Pending)
                .OrderBy(payment => payment.CreatedAt)
                .FirstOrDefault();

            var remainingAmount = amount;
            if (pending is not null)
            {
                var applied = Math.Min(remainingAmount, pending.Amount);
                pending.Status = PaymentStatus.Confirmed;
                pending.Amount = applied;
                pending.Provider = Provider;
                pending.ProviderTransactionId = eventId;
                pending.Reference = Truncate(referenceCode ?? content, 300);
                pending.PaidAt = DateTimeOffset.UtcNow;
                remainingAmount -= applied;
            }

            if (remainingAmount > 0)
            {
                order.Payments.Add(new Payment
                {
                    TenantId = order.TenantId,
                    Method = PaymentMethod.Transfer,
                    Status = PaymentStatus.Confirmed,
                    Amount = OrderEndpoints.Round(remainingAmount),
                    Provider = Provider,
                    ProviderTransactionId = eventId,
                    Reference = Truncate(referenceCode ?? content, 300),
                    PaidAt = DateTimeOffset.UtcNow,
                });
            }

            OrderEndpoints.ApplyPaymentTotals(order);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new WebhookResult(WebhookOutcome.Handled, "Đã xác nhận thanh toán.", order.Code, amount, order.Status);
        }
        catch (DbUpdateException)
        {
            // UNIQUE(Provider, EventId) hoặc lỗi ghi khác: rollback để không cộng tiền hai lần.
            await transaction.RollbackAsync(ct);
            return new WebhookResult(WebhookOutcome.Duplicate, "Giao dịch đã được xử lý (xung đột ghi).", orderCode);
        }
    }

    /// <summary>
    /// Webhook cho QR tại quầy (thanh toán kết hợp): cộng tiền vào phiên QR,
    /// đánh dấu Paid khi đủ; hóa đơn sẽ dùng phiên này lúc bấm THANH TOÁN.
    /// </summary>
    private static async Task<WebhookResult> ProcessPosQrAsync(
        ApplicationDbContext db,
        string reference,
        string eventId,
        decimal amount,
        string rawPayload,
        CancellationToken ct)
    {
        var header = await db.PosQrSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => session.Reference == reference)
            .Select(session => new { session.Id, session.TenantId })
            .FirstOrDefaultAsync(ct);
        if (header is null)
            return new WebhookResult(WebhookOutcome.Unmatched, $"Không có phiên QR {reference}.");

        using var tenantScope = TenantContext.Use(header.TenantId);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var alreadyProcessed = await db.PaymentWebhookEvents
                .AnyAsync(item => item.Provider == Provider && item.EventId == eventId, ct);
            if (alreadyProcessed)
            {
                await transaction.RollbackAsync(ct);
                return new WebhookResult(WebhookOutcome.Duplicate, "Giao dịch đã được xử lý trước đó.", reference);
            }

            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Provider = Provider,
                EventId = eventId,
                ReceivedAt = DateTimeOffset.UtcNow,
                PayloadJson = rawPayload,
            });

            var session = await db.PosQrSessions.FirstOrDefaultAsync(row => row.Id == header.Id, ct);
            if (session is null)
            {
                await transaction.RollbackAsync(ct);
                return new WebhookResult(WebhookOutcome.Unmatched, $"Không đọc được phiên QR {reference}.", reference);
            }

            if (session.Status is PosQrStatus.Paid or PosQrStatus.Used)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new WebhookResult(WebhookOutcome.Ignored, "Phiên QR đã nhận đủ tiền.", reference, amount);
            }

            session.TransferredAmount = (session.TransferredAmount ?? 0) + amount;
            if (session.TransferredAmount >= session.Amount)
            {
                session.Status = PosQrStatus.Paid;
                session.PaidAt = DateTimeOffset.UtcNow;
            }
            else if (session.Status == PosQrStatus.Expired)
            {
                session.Status = PosQrStatus.Pending;
            }

            // Phiên đã gắn vào hóa đơn đang chờ: xác nhận khoản chuyển khoản của đơn,
            // để đơn tự Completed dù khách chuyển sau khi bấm THANH TOÁN.
            if (session.Status == PosQrStatus.Paid && session.OrderId is { } orderId)
            {
                var order = await db.Orders
                    .Include(row => row.Payments)
                    .FirstOrDefaultAsync(row => row.Id == orderId, ct);
                if (order is not null)
                {
                    var pendingPayment = order.Payments.FirstOrDefault(payment =>
                        payment.Status == PaymentStatus.Pending
                        && payment.Method == PaymentMethod.Transfer
                        && payment.Reference == session.Reference);
                    if (pendingPayment is not null)
                    {
                        pendingPayment.Status = PaymentStatus.Confirmed;
                        pendingPayment.PaidAt = DateTimeOffset.UtcNow;
                        pendingPayment.ProviderTransactionId = eventId;
                    }
                    OrderEndpoints.ApplyPaymentTotals(order);
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new WebhookResult(WebhookOutcome.Handled, "Đã cộng tiền vào phiên QR.", reference, amount);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return new WebhookResult(WebhookOutcome.Duplicate, "Giao dịch đã được xử lý (xung đột ghi).", reference);
        }
    }

    private static JsonElement? Find(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        var value = Find(root, name);
        if (value is null) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    private static decimal ReadDecimal(JsonElement root, string name)
    {
        var value = Find(root, name);
        if (value is null) return 0;
        return value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDecimal(out var number)
            ? number
            : 0;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= max ? value
        : value[..max];
}
