using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Security;
using Tryum.Domain.Sales;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Persistence;

namespace Tryum.Api.Endpoints;

/// <summary>
/// Endpoint thanh toán: nhận webhook SePay (chữ ký HMAC-SHA256 / API Key, chống trùng),
/// cấu hình hiển thị cho trang Cài đặt và công cụ giả lập giao dịch khi phát triển.
/// </summary>
public static class PaymentEndpoints
{
    private const int MaxWebhookBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments/sepay").WithTags("Payments – SePay");

        group.MapPost("/webhook", async (
            HttpContext http,
            ApplicationDbContext db,
            IOptions<SePayOptions> options,
            IWebHostEnvironment environment,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("SePayWebhook");
            var config = options.Value;

            if (http.Request.ContentLength > MaxWebhookBytes)
                return Results.BadRequest(new { success = false, message = "Payload quá lớn." });

            // Đọc body thô: SePay ký đúng bytes gốc, parse rồi serialize lại sẽ lệch chữ ký.
            http.Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(
                http.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }
            http.Request.Body.Position = 0;

            if (rawBody.Length == 0)
                return Results.BadRequest(new { success = false, message = "Payload rỗng." });

            // ── Xác thực ───────────────────────────────────────────────────────
            if (config.IsWebhookSecured)
            {
                var valid = !string.IsNullOrWhiteSpace(config.WebhookSecret)
                    ? SePayWebhookVerifier.VerifySignature(
                        config.WebhookSecret,
                        http.Request.Headers[SePayWebhookVerifier.TimestampHeader].ToString(),
                        rawBody,
                        http.Request.Headers[SePayWebhookVerifier.SignatureHeader].ToString(),
                        config.TimestampToleranceSeconds,
                        DateTimeOffset.UtcNow)
                    : SePayWebhookVerifier.VerifyApiKey(
                        config.ApiKey,
                        http.Request.Headers.Authorization.ToString());

                if (!valid)
                {
                    logger.LogWarning("Webhook SePay bị từ chối: chữ ký/API key không hợp lệ từ {RemoteIp}.",
                        http.Connection.RemoteIpAddress);
                    return Results.Json(new { success = false, message = "Invalid signature." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }
            }
            else if (!environment.IsDevelopment())
            {
                logger.LogError("Webhook SePay chưa được cấu hình secret – từ chối ở môi trường {Environment}.",
                    environment.EnvironmentName);
                return Results.Json(
                    new { success = false, message = "Webhook chưa được cấu hình (SePay:WebhookSecret)." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawBody);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { success = false, message = "Payload JSON không hợp lệ." });
            }

            using (document)
            {
                var result = await SePayWebhookProcessor.ProcessAsync(db, document.RootElement, rawBody, ct);
                logger.LogInformation(
                    "Webhook SePay {Outcome}: {Message} (hóa đơn {OrderCode}, {Amount}đ).",
                    result.Outcome, result.Message, result.OrderCode ?? "-", result.Amount);

                // SePay chỉ coi là thành công khi HTTP 200 + body {"success": true}.
                return result.Outcome == WebhookOutcome.Invalid
                    ? Results.BadRequest(new { success = false, message = result.Message })
                    : Results.Ok(new { success = true });
            }
        })
        .AllowAnonymous()
        .WithSummary("Nhận webhook giao dịch ngân hàng từ SePay (xác thực HMAC-SHA256/API Key, chống trùng)")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/settings", (
            HttpRequest http,
            IOptions<SePayOptions> options) =>
        {
            var config = options.Value;
            var baseUrl = string.IsNullOrWhiteSpace(config.PublicApiUrl)
                ? $"{http.Scheme}://{http.Host}"
                : config.PublicApiUrl.TrimEnd('/');

            return Results.Ok(new SePaySettingsResponse(
                WebhookUrl: $"{baseUrl}/api/v1/payments/sepay/webhook",
                WebhookSecured: config.IsWebhookSecured,
                AuthMode: !string.IsNullOrWhiteSpace(config.WebhookSecret) ? "HMAC-SHA256"
                    : !string.IsNullOrWhiteSpace(config.ApiKey) ? "API Key"
                    : "Chưa cấu hình",
                Bank: new SePayBankResponse(
                    Configured: config.IsBankConfigured,
                    Bin: config.BankBin,
                    Name: config.BankName,
                    AccountNumber: MaskAccount(config.AccountNumber),
                    AccountName: config.AccountName),
                VietQrTemplate: config.VietQrTemplate));
        })
        .RequireAuthorization(AuthorizationPolicies.OwnerOrManager)
        .WithSummary("Thông tin cấu hình SePay cho trang Cài đặt (không lộ secret)")
        .Produces<SePaySettingsResponse>();

        group.MapPost("/simulate", async (
            SimulateWebhookRequest body,
            ApplicationDbContext db,
            IOptions<SePayOptions> options,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var allowed = environment.IsDevelopment()
                || configuration.GetValue<bool>("SePay:AllowSimulation");
            if (!allowed)
                return Results.NotFound(new { message = "Chỉ bật khi phát triển (SePay:AllowSimulation)." });

            var code = body.OrderCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new { message = "Thiếu mã hóa đơn cần giả lập." });

            var order = await db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Code == code, ct);
            if (order is null)
                return Results.NotFound(new { message = $"Không tìm thấy hóa đơn {code}." });

            var amount = body.Amount ?? OrderEndpoints.Round(Math.Max(0, order.Total - order.PaidAmount));
            if (amount <= 0)
                return Results.BadRequest(new { message = "Hóa đơn đã thu đủ tiền." });

            var payload = new JsonObject
            {
                ["id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["gateway"] = options.Value.BankName ?? "Vietcombank",
                ["transactionDate"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7))
                    .ToString("yyyy-MM-dd HH:mm:ss"),
                ["accountNumber"] = options.Value.AccountNumber ?? "0000000000",
                ["subAccount"] = string.Empty,
                ["code"] = code,
                ["content"] = $"{code} chuyen tien (gia lap)",
                ["transferType"] = "in",
                ["description"] = "Giao dịch giả lập từ trang quản trị Tryum",
                ["transferAmount"] = (long)amount,
                ["accumulated"] = 0,
                ["referenceCode"] = $"SIM{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            };
            var raw = payload.ToJsonString();

            using var document = JsonDocument.Parse(raw);
            var result = await SePayWebhookProcessor.ProcessAsync(db, document.RootElement, raw, ct);
            return Results.Ok(new
            {
                success = result.Outcome is WebhookOutcome.Handled or WebhookOutcome.Duplicate,
                outcome = result.Outcome.ToString(),
                message = result.Message,
                orderCode = result.OrderCode,
                amount = result.Amount,
                status = result.Status?.ToString(),
            });
        })
        .RequireAuthorization(AuthorizationPolicies.OwnerOrManager)
        .WithSummary("Giả lập một giao dịch chuyển khoản (chỉ dùng khi phát triển)")
        .Produces(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>Che số tài khoản khi trả về FE: giữ 4 số cuối.</summary>
    private static string? MaskAccount(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber)) return null;
        var digits = accountNumber.Trim();
        if (digits.Length <= 4) return new string('*', digits.Length);
        return new string('*', digits.Length - 4) + digits[^4..];
    }

    public sealed record SimulateWebhookRequest(string? OrderCode, decimal? Amount);

    public sealed record SePayBankResponse(
        bool Configured,
        string? Bin,
        string? Name,
        string? AccountNumber,
        string? AccountName);

    public sealed record SePaySettingsResponse(
        string WebhookUrl,
        bool WebhookSecured,
        string AuthMode,
        SePayBankResponse Bank,
        string VietQrTemplate);
}
