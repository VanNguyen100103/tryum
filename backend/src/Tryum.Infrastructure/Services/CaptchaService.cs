using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Captcha sinh ở SERVER (SVG trả về dạng data-uri, không cần thư viện xử lý ảnh).
/// Vì sao cần: nếu captcha sinh ở client như hiện trước đây, bot chỉ cần gọi thẳng
/// <c>POST /auth/otp/send</c> là đốt hết quota SMS của gateway.
/// Mỗi mã dùng đúng một lần và tự hết hạn sau <see cref="CaptchaOptions.TtlSeconds"/> giây.
/// </summary>
public sealed class CaptchaService(CaptchaOptions? options = null) : ICaptchaService
{
    // Loại bỏ ký tự dễ nhầm (0/O, 1/I/L) để người dùng không bị bắt nhầm.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 4;

    private readonly CaptchaOptions _options = options ?? new CaptchaOptions();
    private readonly ConcurrentDictionary<string, CaptchaEntry> _store = new();

    public CaptchaChallenge Create()
    {
        var now = DateTimeOffset.UtcNow;
        PurgeExpired(now);

        var id = Guid.NewGuid().ToString("N");
        var code = NewCode();

        _store[id] = new CaptchaEntry(Hash(code), now.AddSeconds(_options.TtlSeconds));

        return new CaptchaChallenge(id, BuildSvgDataUrl(code));
    }

    public bool Validate(string? captchaId, string? code)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(code))
            return false;

        // Xoá trước khi kiểm tra => mã chỉ dùng được 1 lần, kể cả khi người dùng nhập sai.
        if (!_store.TryRemove(captchaId, out var entry))
            return false;

        if (entry.IsExpired(DateTimeOffset.UtcNow))
            return false;

        // Người dùng có thể gõ chữ thường => so khớp không phân biệt hoa/thường.
        return FixedTimeEquals(entry.CodeHash, Hash(code.Trim().ToUpperInvariant()));
    }

    private static string NewCode()
    {
        var chars = new char[Length];
        var index = RandomNumberGenerator.GetInt32(Alphabet.Length);

        for (var i = 0; i < Length; i++)
        {
            if (i > 0)
                index = RandomNumberGenerator.GetInt32(Alphabet.Length);

            chars[i] = Alphabet[index];
        }

        return new string(chars);
    }

    /// <summary>
    /// Vẽ mã thành SVG: chữ nghiêng, xoay nhẹ, có nét nhiễu để bot khó đọc.
    /// Trả về data-uri để frontend gán thẳng vào <c>&lt;img src&gt;</c>.
    /// </summary>
    private static string BuildSvgDataUrl(string code)
    {
        var builder = new StringBuilder();
        builder.Append("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='46' viewBox='0 0 120 46'>");
        builder.Append("<rect width='120' height='46' fill='#f4f7fb'/>");

        // Nét nhiễu.
        for (var i = 0; i < 4; i++)
        {
            var y1 = RandomNumberGenerator.GetInt32(6, 40);
            var y2 = RandomNumberGenerator.GetInt32(6, 40);
            builder.Append(
                $"<line x1='0' y1='{y1}' x2='120' y2='{y2}' stroke='#93a7c8' " +
                $"stroke-width='1' opacity='0.4'/>");
        }

        var x = 16;
        foreach (var ch in code)
        {
            var rotate = RandomNumberGenerator.GetInt32(-14, 15);
            var shift = RandomNumberGenerator.GetInt32(-3, 4);
            var y = 33 + shift;

            builder.Append(
                $"<text x='{x}' y='{y}' font-family='Georgia,serif' font-size='27' " +
                $"font-weight='bold' font-style='italic' fill='#17356b' " +
                $"transform='rotate({rotate} {x} {y})'>{ch}</text>");

            x += 24;
        }

        builder.Append("</svg>");

        return "data:image/svg+xml;base64," +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var pair in _store)
        {
            if (pair.Value.IsExpired(now))
                _store.TryRemove(pair.Key, out _);
        }
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));

    /// <summary>Entry lưu hash mã + thời điểm hết hạn (tính sẵn lúc sinh).</summary>
    private sealed record CaptchaEntry(string CodeHash, DateTimeOffset ExpiresAtUtc)
    {
        public bool IsExpired(DateTimeOffset now) => now > ExpiresAtUtc;
    }
}