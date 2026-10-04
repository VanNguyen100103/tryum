using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Gửi SMS qua "SMS Gate for Android" (self-host, MIỄN PHÍ – https://sms-gate.app):
/// điện thoại Android của bạn đóng vai SMS gateway, gọi qua HTTP + Basic Auth.
/// Đổi <c>Sms:SmsGate:SendUrl</c> sang địa chỉ máy để dùng chế độ Local, giữ nguyên để dùng Cloud.
/// </summary>
public sealed class SmsGateSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsOptions> options,
    ILogger<SmsGateSmsSender>? logger = null) : ISmsSender
{
    /// <summary>Tên HttpClient đăng ký trong DI (chỉ để cấu hình timeout).</summary>
    public const string ClientName = "smsgate";

    public async Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        var gate = options.Value.SmsGate;

        if (string.IsNullOrWhiteSpace(gate.SendUrl))
        {
            throw new AppException(
                "Chưa cấu hình Sms:SmsGate:SendUrl.",
                HttpStatusCode.BadGateway,
                "sms_not_configured");
        }

        var payload = new Dictionary<string, object?>
        {
            ["textMessage"] = new { text = message },
            ["phoneNumbers"] = new[] { phoneE164 },
        };

        if (gate.SimNumber is > 0)
            payload["simNumber"] = gate.SimNumber;

        if (gate.WithDeliveryReport)
            payload["withDeliveryReport"] = true;

        using var request = new HttpRequestMessage(HttpMethod.Post, gate.SendUrl)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(gate.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{gate.Username}:{gate.Password}"));

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        var client = httpClientFactory.CreateClient(ClientName);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger?.LogError(
                    "SMSGate từ chối gửi tới {Phone} ({Status}): {Body}",
                    phoneE164, (int)response.StatusCode, body);

                throw new AppException(
                    $"Không gửi được SMS (SMSGate {(int)response.StatusCode}). Vui lòng thử lại.",
                    HttpStatusCode.BadGateway,
                    "sms_send_failed");
            }

            logger?.LogInformation("Đã gửi SMS tới {Phone} qua SMSGate.", phoneE164);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "Không kết nối được SMSGate khi gửi tới {Phone}.", phoneE164);

            throw new AppException(
                "Không kết nối được cổng gửi SMS. Vui lòng thử lại.",
                HttpStatusCode.BadGateway,
                "sms_send_failed");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(ex, "SMSGate timeout khi gửi tới {Phone}.", phoneE164);

            throw new AppException(
                "Cổng gửi SMS phản hồi quá lâu. Vui lòng thử lại.",
                HttpStatusCode.BadGateway,
                "sms_send_failed");
        }
    }
}
