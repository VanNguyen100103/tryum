using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Gửi SMS qua textbee.dev (https://textbee.dev – free tier: 1 máy, 50 tin/ngày, 300 tin/tháng):
/// điện thoại Android chạy app textbee làm gateway, API gọi POST /api/v1/gateway/send-sms với
/// header <c>x-api-key</c> để nhận SMS trả về. Điện thoại phải bật và online thì tin mới đi.
/// </summary>
public sealed class TextbeeSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsOptions> options,
    ILogger<TextbeeSmsSender>? logger = null) : ISmsSender
{
    /// <summary>Tên HttpClient đăng ký trong DI (chỉ để cấu hình timeout).</summary>
    public const string ClientName = "textbee";

    public async Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        var textbee = options.Value.Textbee;

        if (string.IsNullOrWhiteSpace(textbee.ApiKey))
        {
            throw new AppException(
                "Chưa cấu hình Sms:Textbee:ApiKey.",
                HttpStatusCode.BadGateway,
                "sms_not_configured");
        }

        // textbee yêu cầu: message + recipients (E.164). deviceId/sim là tuỳ chọn.
        var payload = new Dictionary<string, object?>
        {
            ["recipients"] = new[] { phoneE164 },
            ["message"] = message,
        };

        if (!string.IsNullOrWhiteSpace(textbee.DeviceId))
            payload["deviceId"] = textbee.DeviceId;

        if (textbee.SimSubscriptionId is > 0)
            payload["simSubscriptionId"] = textbee.SimSubscriptionId.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, textbee.SendUrl)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.TryAddWithoutValidation("x-api-key", textbee.ApiKey);

        var client = httpClientFactory.CreateClient(ClientName);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger?.LogError(
                    "Textbee từ chối gửi tới {Phone} ({Status}): {Body}",
                    phoneE164, (int)response.StatusCode, body);

                throw new AppException(
                    $"Không gửi được SMS (textbee {(int)response.StatusCode}). Vui lòng thử lại.",
                    HttpStatusCode.BadGateway,
                    "sms_send_failed");
            }

            // 200 = textbee đã nhận batch và đẩy về máy. Trạng thái giao thật xem /gateway/messages.
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger?.LogInformation(
                "Đã gửi SMS tới {Phone} qua textbee: {Response}",
                phoneE164, responseBody);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Không kết nối được textbee khi gửi tới {Phone}.", phoneE164);

            throw new AppException(
                "Không kết nối được cổng gửi SMS. Vui lòng thử lại.",
                HttpStatusCode.BadGateway,
                "sms_send_failed");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(ex, "textbee timeout khi gửi tới {Phone}.", phoneE164);

            throw new AppException(
                "Cổng gửi SMS phản hồi quá lâu. Vui lòng thử lại.",
                HttpStatusCode.BadGateway,
                "sms_send_failed");
        }
    }
}