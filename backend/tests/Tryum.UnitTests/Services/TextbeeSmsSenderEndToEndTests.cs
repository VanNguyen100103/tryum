using System.Text.Json;
using Microsoft.Extensions.Options;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Services;

/// <summary>
/// Kiểm chứng end-to-end <see cref="TextbeeSmsSender"/> với API thật của textbee.dev.
/// Chỉ chạy khi có biến môi trường <c>TEXTBEE_E2E_API_KEY</c> + <c>TEXTBEE_E2E_TO</c>
/// (bỏ trống = test bị bỏ qua) – không làm cháy quota khi CI chạy.
/// </summary>
public class TextbeeSmsSenderEndToEndTests
{
    [Fact]
    public async Task SendAsync_RealApi_AcceptsMessage()
    {
        var apiKey = Environment.GetEnvironmentVariable("TEXTBEE_E2E_API_KEY");
        var to = Environment.GetEnvironmentVariable("TEXTBEE_E2E_TO");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(to))
            return; // Không có cấu hình -> bỏ qua âm thầm, không làm cháy quota CI.

        var options = Options.Create(new SmsOptions
        {
            Provider = "Textbee",
            Textbee = new TextbeeOptions
            {
                ApiKey = apiKey,
                SendUrl = "https://api.textbee.dev/api/v1/gateway/send-sms",
                DeviceId = Environment.GetEnvironmentVariable("TEXTBEE_E2E_DEVICE_ID"),
            },
        });

        using var http = new HttpClient();
        var sender = new TextbeeSmsSender(
            new SingleClientFactory(http), options);

        // Tin có mã ngẫu nhiên để không đụng cooldown của app khi chạy lại nhiều lần.
        var code = Random.Shared.Next(100000, 999999).ToString();
        await sender.SendAsync(to!, $"Tryum E2E test. Ma: {code}");

        // Chờ máy Android nhận và gửi, rồi đối soát trạng thái thật trên textbee.
        await Task.Delay(TimeSpan.FromSeconds(15));

        var key = apiKey!;
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get, "https://api.textbee.dev/api/v1/gateway/messages?limit=5");
        listRequest.Headers.TryAddWithoutValidation("x-api-key", key);

        var response = await http.SendAsync(listRequest);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        // Tin vừa gửi phải xuất hiện với mã OTP, trạng thái không phải failed.
        Assert.Contains(code, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\":\"failed\"", json, StringComparison.Ordinal);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}