using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Security;
using Tryum.Domain.Sales;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>
/// Phiên QR tại quầy cho thanh toán kết hợp: tạo mã theo số tiền còn thiếu,
/// FE poll trạng thái, SePay webhook khớp theo nội dung POSxxxxxxxxx.
/// </summary>
public static class PosQrEndpoints
{
    private const int ExpirySeconds = 900;

    public static IEndpointRouteBuilder MapPosQrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments/pos")
            .WithTags("Payments – POS QR")
            .RequireAuthorization(AuthorizationPolicies.Authenticated);

        group.MapPost("/qr", async (
            CreatePosQrRequest request,
            ApplicationDbContext db,
            IOptions<SePayOptions> sepayOptions,
            CancellationToken ct) =>
        {
            var amount = OrderEndpoints.Round(request.Amount);
            if (amount <= 0)
                return Results.BadRequest(new { message = "Số tiền QR phải lớn hơn 0." });

            var options = sepayOptions.Value;
            if (!options.IsBankConfigured)
            {
                return Results.Conflict(new
                {
                    message = "Chưa cấu hình tài khoản ngân hàng nhận tiền (SePay:BankBin / SePay:AccountNumber).",
                });
            }

            var session = new PosQrSession
            {
                Amount = amount,
                Status = PosQrStatus.Pending,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ExpirySeconds),
                Reference = await NextReferenceAsync(db, ct),
            };
            db.PosQrSessions.Add(session);
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(session, options));
        })
        .WithSummary("Tạo phiên QR ở quầy theo số tiền cần thu")
        .Produces<PosQrResponse>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/qr/{id:guid}", async (
            Guid id,
            ApplicationDbContext db,
            IOptions<SePayOptions> sepayOptions,
            CancellationToken ct) =>
        {
            var session = await db.PosQrSessions.FirstOrDefaultAsync(row => row.Id == id, ct);
            if (session is null)
                return Results.NotFound(new { message = "Không tìm thấy phiên QR." });

            if (session.Status == PosQrStatus.Pending && session.ExpiresAt < DateTimeOffset.UtcNow)
            {
                session.Status = PosQrStatus.Expired;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(ToResponse(session, sepayOptions.Value));
        })
        .WithSummary("Đọc trạng thái phiên QR (FE poll để tự cộng tiền khi webhook báo)")
        .Produces<PosQrResponse>();

        group.MapPost("/qr/{id:guid}/confirm", async (
            Guid id,
            ApplicationDbContext db,
            IOptions<SePayOptions> sepayOptions,
            CancellationToken ct) =>
        {
            var session = await db.PosQrSessions.FirstOrDefaultAsync(row => row.Id == id, ct);
            if (session is null)
                return Results.NotFound(new { message = "Không tìm thấy phiên QR." });
            if (session.Status == PosQrStatus.Used)
                return Results.BadRequest(new { message = "Phiên QR đã được dùng cho hóa đơn khác." });

            session.Status = PosQrStatus.Paid;
            session.TransferredAmount = session.Amount;
            session.PaidAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(session, sepayOptions.Value));
        })
        .WithSummary("Xác nhận thủ công khi khách đã chuyển khoản nhưng webhook chưa tới")
        .Produces<PosQrResponse>();

        return app;
    }

    private static async Task<string> NextReferenceAsync(ApplicationDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var reference = $"POS{Random.Shared.Next(100_000_000, 999_999_999)}";
            var exists = await db.PosQrSessions.IgnoreQueryFilters()
                .AnyAsync(row => row.Reference == reference, ct);
            if (!exists) return reference;
        }

        return $"POS{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000:D9}";
    }

    private static PosQrResponse ToResponse(PosQrSession session, SePayOptions options) => new(
        session.Id,
        session.Reference,
        session.Amount,
        VietQrUrlBuilder.Build(options, session.Amount, session.Reference),
        session.Status,
        session.TransferredAmount,
        session.PaidAt,
        Math.Max(0, (int)Math.Ceiling((session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)));

    public sealed record CreatePosQrRequest(decimal Amount);

    public sealed record PosQrResponse(
        Guid Id,
        string Reference,
        decimal Amount,
        string QrUrl,
        PosQrStatus Status,
        decimal? TransferredAmount,
        DateTimeOffset? PaidAt,
        int ExpiresInSeconds);
}
