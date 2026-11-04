using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// Captcha sinh ở SERVER (SVG data-uri). Mỗi mã dùng đúng một lần và tự hết hạn.
/// Store ưu tiên Redis dùng chung để nhiều instance API chia sẻ; khi Redis chưa cấu hình
/// thì fallback in-memory (dev/test 1 instance).
/// </summary>
public sealed class CaptchaService(CaptchaOptions? options = null, IRedisStore? redisStore = null) : ICaptchaService
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 4;

    private readonly CaptchaOptions _options = options ?? new CaptchaOptions();
    private readonly IRedisStore? _redis = redisStore is { IsConfigured: true } ? redisStore : null;
    private readonly ConcurrentDictionary<string, CaptchaEntry> _store = new();

    public CaptchaChallenge Create()
    {
        var now = DateTimeOffset.UtcNow;

        if (_redis is not null)
            return CreateViaRedis(now);

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

        if (_redis is not null)
            return ValidateViaRedis(captchaId, code);

        if (!_store.TryRemove(captchaId, out var entry))
            return false;

        if (entry.IsExpired(DateTimeOffset.UtcNow))
            return false;

        return FixedTimeEquals(entry.CodeHash, Hash(code.Trim().ToUpperInvariant()));
    }

    // ── Redis path ────────────────────────────────────────────────────────────

    private CaptchaChallenge CreateViaRedis(DateTimeOffset now)
    {
        var id = Guid.NewGuid().ToString("N");
        var code = NewCode();

        _redis!.SetAsync($"captcha:{id}", new CaptchaEntry(Hash(code), now.AddSeconds(_options.TtlSeconds)),
            TimeSpan.FromSeconds(_options.TtlSeconds)).GetAwaiter().GetResult();

        return new CaptchaChallenge(id, BuildSvgDataUrl(code));
    }

    private bool ValidateViaRedis(string captchaId, string code)
    {
        var entry = _redis!.GetAndRemoveAsync<CaptchaEntry>($"captcha:{captchaId}").GetAwaiter().GetResult();
        if (entry is null || entry.IsExpired(DateTimeOffset.UtcNow))
            return false;

        return FixedTimeEquals(entry.CodeHash, Hash(code.Trim().ToUpperInvariant()));
    }

    // ── Dùng chung ────────────────────────────────────────────────────────────

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

    private static string BuildSvgDataUrl(string code)
    {
        var builder = new StringBuilder();
        builder.Append("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='46' viewBox='0 0 120 46'>");
        builder.Append("<rect width='120' height='46' fill='#f4f7fb'/>");

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

    private sealed class CaptchaEntry
    {
        public string CodeHash { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; set; }

        public CaptchaEntry() { }

        public CaptchaEntry(string codeHash, DateTimeOffset expiresAtUtc)
        {
            CodeHash = codeHash;
            ExpiresAtUtc = expiresAtUtc;
        }

        public bool IsExpired(DateTimeOffset now) => now > ExpiresAtUtc;
    }
}
