using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Exceptions;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Services;

/// <summary>
/// Kiểm tra <see cref="TextbeeSmsSender"/> dựng đúng HTTP request và xử lý lỗi
/// (fail-fast khi thiếu API key, map lỗi gateway thành AppException).
/// </summary>
public class TextbeeSmsSenderTests
{
    private const string SendUrl = "https://api.textbee.dev/api/v1/gateway/send-sms";

    [Fact]
    public async Task SendAsync_PostsRecipientsAndMessage()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"data\":{\"success\":true,\"smsBatchId\":\"66b1f2c3\",\"recipientCount\":1}}"));
        var sender = CreateSender(handler);

        await sender.SendAsync("+84901234567", "Ma OTP: 123456");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.RequestUri!.ToString());

        using var json = JsonDocument.Parse(request.Body!);
        Assert.Equal("Ma OTP: 123456", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            "+84901234567",
            Assert.Single(json.RootElement.GetProperty("recipients").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task SendAsync_SendsApiKeyInXApiKeyHeader()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"data\":{\"success\":true}}"));
        var sender = CreateSender(handler, apiKey: "test-secret-key");

        await sender.SendAsync("+84901234567", "hello");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("test-secret-key", request.ApiKey);
    }

    [Fact]
    public async Task SendAsync_IncludesDeviceIdAndSim_WhenConfigured()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"data\":{\"success\":true}}"));
        var sender = CreateSender(handler, deviceId: "6ac2112d48861886cf4ebbee", simSubscriptionId: 2);

        await sender.SendAsync("+84901234567", "hello");

        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("6ac2112d48861886cf4ebbee", json.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("simSubscriptionId").GetInt32());
    }

    [Fact]
    public async Task SendAsync_OmitsDeviceIdAndSim_WhenNotConfigured()
    {
        var handler = new StubHandler(_ => JsonResponse("{\"data\":{\"success\":true}}"));
        var sender = CreateSender(handler);

        await sender.SendAsync("+84901234567", "hello");

        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.False(json.RootElement.TryGetProperty("deviceId", out _));
        Assert.False(json.RootElement.TryGetProperty("simSubscriptionId", out _));
    }

    [Fact]
    public async Task SendAsync_ThrowsNotConfigured_WhenApiKeyMissing()
    {
        var handler = new StubHandler(_ => JsonResponse("{}"));
        var sender = CreateSender(handler, apiKey: "");

        var ex = await Assert.ThrowsAsync<AppException>(
            async () => await sender.SendAsync("+84901234567", "hello"));

        Assert.Equal("sms_not_configured", ex.ErrorCode);
        Assert.Empty(handler.Requests); // không gọi mạng khi thiếu cấu hình
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task SendAsync_ThrowsSendFailed_OnGatewayError(int statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent("{\"error\":\"Unauthorized\"}", Encoding.UTF8, "application/json"),
        });
        var sender = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<AppException>(
            async () => await sender.SendAsync("+84901234567", "hello"));

        Assert.Equal("sms_send_failed", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ThrowsSendFailed_WhenNetworkUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no connection"));
        var sender = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<AppException>(
            async () => await sender.SendAsync("+84901234567", "hello"));

        Assert.Equal("sms_send_failed", ex.ErrorCode);
    }

    private static TextbeeSmsSender CreateSender(
        StubHandler handler,
        string apiKey = "test-key",
        string? deviceId = null,
        int? simSubscriptionId = null)
    {
        var options = Options.Create(new SmsOptions
        {
            Provider = "Textbee",
            Textbee = new TextbeeOptions
            {
                ApiKey = apiKey,
                SendUrl = SendUrl,
                DeviceId = deviceId,
                SimSubscriptionId = simSubscriptionId,
            },
        });

        return new TextbeeSmsSender(new StubHttpClientFactory(handler), options);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Ghi lại request để kiểm tra. Nội dung body phải đọc ngay trong SendAsync vì sender
    /// dispose <c>HttpRequestMessage</c> ngay khi gửi xong.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public string? LastBody => Requests.Count == 0 ? null : Requests[^1].Body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.TryGetValues("x-api-key", out var key) ? Assert.Single(key) : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? ApiKey, string? Body);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
