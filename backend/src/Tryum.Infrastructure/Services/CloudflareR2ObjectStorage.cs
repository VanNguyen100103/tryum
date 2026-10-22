using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

public sealed class CloudflareR2ObjectStorage(
    HttpClient httpClient,
    IOptions<R2Options> options) : IObjectStorage
{
    private const string Region = "auto";
    private const string Service = "s3";
    private readonly R2Options _options = options.Value;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        using var request = CreateRequest(HttpMethod.Put, key, bytes, contentType);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Get, key, [], "application/octet-stream");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = new MemoryStream();
        await response.Content.CopyToAsync(result, cancellationToken);
        result.Position = 0;
        return result;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string key, byte[] payload, string contentType)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = $"{_options.AccountId}.r2.cloudflarestorage.com";
        var canonicalUri = $"/{Uri.EscapeDataString(_options.BucketName)}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var canonicalHeaders = method == HttpMethod.Put
            ? $"content-type:{contentType.Trim()}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n"
            : $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        var signedHeaders = method == HttpMethod.Put
            ? "content-type;host;x-amz-content-sha256;x-amz-date"
            : "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method.Method}\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/{Region}/{Service}/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{HashHex(Encoding.UTF8.GetBytes(canonicalRequest))}";
        var signature = Sign(stringToSign, date);

        var request = new HttpRequestMessage(method, $"{_options.Endpoint}{canonicalUri}");
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={_options.AccessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        if (method == HttpMethod.Put)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new(contentType);
        }
        return request;
    }

    private string Sign(string stringToSign, string date)
    {
        var dateKey = Hmac(Encoding.UTF8.GetBytes($"AWS4{_options.SecretAccessKey}"), date);
        var regionKey = Hmac(dateKey, Region);
        var serviceKey = Hmac(regionKey, Service);
        var signingKey = Hmac(serviceKey, "aws4_request");
        return Convert.ToHexString(Hmac(signingKey, stringToSign)).ToLowerInvariant();
    }

    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string HashHex(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("R2 chưa được cấu hình. Hãy đặt R2__AccountId, R2__AccessKeyId, R2__SecretAccessKey và R2__BucketName.");
    }
}
