using System.Security.Cryptography;
using System.Text;

namespace Tryum.Infrastructure.Common;

/// <summary>
/// Xác thực webhook SePay theo tài liệu https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc
/// - HMAC-SHA256: ký chuỗi <c>{timestamp}.{raw_body}</c>, header <c>X-SePay-Signature: sha256={hex}</c>
///   + <c>X-SePay-Timestamp</c>; từ chối request lệch quá 5 phút để chống replay.
/// - API Key: header <c>Authorization: Apikey {key}</c>.
/// Tách thành hàm thuần để unit test không cần HTTP.
/// </summary>
public static class SePayWebhookVerifier
{
    public const string SignatureHeader = "X-SePay-Signature";
    public const string TimestampHeader = "X-SePay-Timestamp";

    /// <summary>Tính chữ ký theo đúng công thức SePay (dùng cho test và công cụ giả lập).</summary>
    public static string ComputeSignature(string secret, string timestamp, string rawBody)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Kiểm tra chữ ký HMAC-SHA256. Trả về false nếu thiếu header, sai chữ ký
    /// hoặc timestamp quá cũ/mới (chống replay).
    /// </summary>
    public static bool VerifySignature(
        string? secret,
        string? timestampHeader,
        string? rawBody,
        string? signatureHeader,
        int toleranceSeconds,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(timestampHeader)
            || string.IsNullOrWhiteSpace(signatureHeader)
            || rawBody is null)
        {
            return false;
        }

        if (!long.TryParse(timestampHeader, out var timestamp))
            return false;

        var tolerance = Math.Max(0, toleranceSeconds);
        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance)
            return false;

        var expected = ComputeSignature(secret, timestampHeader, rawBody);

        // So sánh constant-time, không phân biệt hoa/thường.
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(signatureHeader.Trim().ToLowerInvariant());
        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    /// <summary>Kiểm tra header <c>Authorization: Apikey {key}</c> theo kiểu so sánh constant-time.</summary>
    public static bool VerifyApiKey(string? expectedApiKey, string? authorizationHeader)
    {
        const string prefix = "Apikey ";
        if (string.IsNullOrWhiteSpace(expectedApiKey)
            || string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = authorizationHeader[prefix.Length..].Trim();
        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
