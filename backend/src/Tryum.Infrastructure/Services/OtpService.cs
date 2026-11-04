using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Tryum.Application.Common;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// OTP: TTL + cooldown + giới hạn số lần đoán sai + giới hạn số tin/ngày mỗi SĐT,
/// mã 6 số sinh bằng crypto random và chỉ lưu SHA-256.
/// Store ưu tiên Redis dùng chung (Upstash) để nhiều instance API chia sẻ; khi Redis chưa cấu hình
/// thì tự fallback về in-memory (phục vụ dev/test 1 instance).
/// </summary>
public sealed class OtpService(
    ISmsSender? smsSender = null,
    ILogger<OtpService>? logger = null,
    OtpOptions? options = null,
    IRedisStore? redisStore = null) : IOtpService
{
    private static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);

    private readonly OtpOptions _options = options ?? new OtpOptions();
    private readonly IRedisStore? _redis = redisStore is { IsConfigured: true } ? redisStore : null;

    // ── Fallback in-memory (khi không có Redis) ──────────────────────────────
    private readonly ConcurrentDictionary<string, OtpEntry> _store = new();
    private readonly ConcurrentDictionary<string, SendCounter> _counters = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _locks = new();

    public async Task<OtpIssueResult> IssueAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);
        var now = DateTimeOffset.UtcNow;

        if (_redis is not null)
            return await IssueViaRedisAsync(e164, now, cancellationToken);
        return await IssueInMemoryAsync(e164, now, cancellationToken);
    }

    /// <summary>SĐT đang có mã OTP chờ xác nhận hay không (chưa hết hạn).</summary>
    public bool HasPendingCode(string phone)
    {
        if (!PhoneNormalizer.TryNormalize(phone, out var e164))
            return false;

        if (_redis is not null)
        {
            var entry = _redis.GetAsync<OtpEntry>(OtpKey(e164)).GetAwaiter().GetResult();
            return entry is not null && !entry.IsExpired(DateTimeOffset.UtcNow);
        }

        return _store.TryGetValue(e164, out var memEntry)
               && !memEntry.IsExpired(DateTimeOffset.UtcNow);
    }

    public async Task ConsumeAsync(string phone, string code, CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);
        var now = DateTimeOffset.UtcNow;

        if (_redis is not null)
        {
            await ConsumeViaRedisAsync(e164, code, now, cancellationToken);
            return;
        }

        ConsumeInMemory(e164, code, now);
    }

    /// <summary>Đối soát khi muốn kiểm tra thử (test).</summary>
    internal OtpEntry? Peek(string phone)
    {
        if (!PhoneNormalizer.TryNormalize(phone, out var e164))
            return null;

        if (_redis is not null)
            return _redis.GetAsync<OtpEntry>(OtpKey(e164)).GetAwaiter().GetResult();

        return _store.TryGetValue(e164, out var entry) ? entry : null;
    }

    // ── Redis path ────────────────────────────────────────────────────────────

    private async Task<OtpIssueResult> IssueViaRedisAsync(string e164, DateTimeOffset now, CancellationToken ct)
    {
        var otpKey = OtpKey(e164);
        var lockKey = LockKey(e164);
        var countKey = CountKey(e164);

        // 1) Khoá do đoán sai.
        var lockRaw = await _redis!.GetStringAsync(lockKey, ct);
        if (DateTimeOffset.TryParse(lockRaw, out var untilLocked) && now <= untilLocked)
        {
            throw new RateLimitedException(
                "Bạn đã nhập sai mã quá nhiều lần. Vui lòng gửi mã OTP mới sau ít phút.",
                "otp_locked");
        }
        if (lockRaw is not null)
            await _redis.RemoveAsync(lockKey, ct);

        // 2) Cooldown.
        var existing = await _redis.GetAsync<OtpEntry>(otpKey, ct);
        if (existing is not null && !existing.IsExpired(now))
        {
            var elapsed = now - existing.IssuedAtUtc;
            var cooldown = TimeSpan.FromSeconds(_options.CooldownSeconds);
            if (elapsed < cooldown)
            {
                var wait = (int)Math.Ceiling((cooldown - elapsed).TotalSeconds);
                throw new RateLimitedException(
                    $"Vui lòng chờ {wait} giây nữa rồi gửi lại mã OTP.",
                    "otp_cooldown");
            }
        }

        // 3) Giới hạn tin/24h.
        var sent = await _redis.GetCounterAsync(countKey, ct);
        if (sent >= _options.MaxSendsPerDay)
            throw new RateLimitedException(
                "Bạn đã yêu cầu quá nhiều mã OTP. Vui lòng thử lại sau 24 giờ.",
                "otp_daily_limit");

        var code = NewCode();
        await _redis.SetAsync(otpKey, new OtpEntry
        {
            CodeHash = HashCode(code),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(_options.TtlSeconds),
            Attempts = 0,
        }, TimeSpan.FromSeconds(_options.TtlSeconds), ct);

        var message = $"Tryum: Ma OTP dang ky cua ban la {code}. " +
                      $"Hieu luc {_options.TtlSeconds / 60} phut. Khong chia se ma nay.";
        await SendAsync(e164, code, message, ct);

        var newCount = await _redis.IncrementAsync(countKey, ct);
        if (newCount == 1)
            await _redis.SetExpiryAsync(countKey, DailyWindow, ct);

        return new OtpIssueResult(code, _options.CooldownSeconds, _options.TtlSeconds);
    }

    private async Task ConsumeViaRedisAsync(string e164, string code, DateTimeOffset now, CancellationToken ct)
    {
        var otpKey = OtpKey(e164);
        var entry = await _redis!.GetAsync<OtpEntry>(otpKey, ct);

        if (entry is null || entry.IsExpired(now))
        {
            await _redis.RemoveAsync(otpKey, ct);
            throw new ValidationException(
                "Mã OTP chưa được gửi hoặc đã hết hạn. Vui lòng gửi lại mã mới.",
                "otp_expired");
        }

        if (!FixedTimeEquals(entry.CodeHash, HashCode(code.Trim())))
        {
            entry.Attempts++;

            if (entry.Attempts >= _options.MaxAttempts)
            {
                await _redis.SetStringAsync(LockKey(e164),
                    now.AddSeconds(_options.TtlSeconds).ToUniversalTime().ToString("O"),
                    TimeSpan.FromSeconds(_options.TtlSeconds), ct);
                await _redis.RemoveAsync(otpKey, ct);
                throw new RateLimitedException(
                    "Quá số lần thử sai. Vui lòng gửi mã OTP mới.",
                    "otp_locked");
            }

            var remaining = entry.ExpiresAtUtc - now;
            await _redis.SetAsync(otpKey, entry, remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1), ct);
            throw new ValidationException("Mã OTP không đúng.", "otp_invalid");
        }

        await _redis.RemoveAsync(otpKey, ct);
    }

    // ── In-memory path (fallback dev/test) ────────────────────────────────────

    private async Task<OtpIssueResult> IssueInMemoryAsync(string e164, DateTimeOffset now, CancellationToken ct)
    {
        PurgeExpired();

        EnsureNotLockedOut(e164, now);
        EnsureCooldown(e164, now);
        EnsureDailyQuota(e164, now);

        var code = NewCode();

        _store[e164] = new OtpEntry
        {
            CodeHash = HashCode(code),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddSeconds(_options.TtlSeconds),
            Attempts = 0,
        };

        var message = $"Tryum: Ma OTP dang ky cua ban la {code}. " +
                      $"Hieu luc {_options.TtlSeconds / 60} phut. Khong chia se ma nay.";
        await SendAsync(e164, code, message, ct);

        _counters.GetOrAdd(e164, _ => new SendCounter()).Record(now);

        return new OtpIssueResult(code, _options.CooldownSeconds, _options.TtlSeconds);
    }

    private void ConsumeInMemory(string e164, string code, DateTimeOffset now)
    {
        if (!_store.TryGetValue(e164, out var entry) || entry.IsExpired(now))
        {
            _store.TryRemove(e164, out _);
            throw new ValidationException(
                "Mã OTP chưa được gửi hoặc đã hết hạn. Vui lòng gửi lại mã mới.",
                "otp_expired");
        }

        if (!FixedTimeEquals(entry.CodeHash, HashCode(code.Trim())))
        {
            entry.Attempts++;

            if (entry.Attempts >= _options.MaxAttempts)
            {
                _locks[e164] = now.AddSeconds(_options.TtlSeconds);
                _store.TryRemove(e164, out _);
                throw new RateLimitedException(
                    "Quá số lần thử sai. Vui lòng gửi mã OTP mới.",
                    "otp_locked");
            }

            throw new ValidationException("Mã OTP không đúng.", "otp_invalid");
        }

        _store.TryRemove(e164, out _);
    }

    // ── Dùng chung ────────────────────────────────────────────────────────────

    private async Task SendAsync(string e164, string code, string message, CancellationToken ct)
    {
        if (smsSender is not null)
            await smsSender.SendAsync(e164, message, ct);
        else
            logger?.LogInformation(
                "OTP demo (chưa cấu hình SMS sender) cho {Phone}: {Code} (hết hạn sau {Ttl}s)",
                e164, code, _options.TtlSeconds);
    }

    private static string OtpKey(string e164) => $"otp:{e164}";
    private static string LockKey(string e164) => $"otp:lock:{e164}";
    private static string CountKey(string e164) => $"otp:count:{e164}";

    private void EnsureNotLockedOut(string e164, DateTimeOffset now)
    {
        if (_locks.TryGetValue(e164, out var untilUtc))
        {
            if (now <= untilUtc)
                throw new RateLimitedException(
                    "Bạn đã nhập sai mã quá nhiều lần. Vui lòng gửi mã OTP mới sau ít phút.",
                    "otp_locked");

            _locks.TryRemove(e164, out _);
        }
    }

    private void EnsureCooldown(string e164, DateTimeOffset now)
    {
        if (!_store.TryGetValue(e164, out var entry) || entry.IsExpired(now))
            return;

        var cooldown = TimeSpan.FromSeconds(_options.CooldownSeconds);
        var elapsed = now - entry.IssuedAtUtc;

        if (elapsed < cooldown)
        {
            var wait = (int)Math.Ceiling((cooldown - elapsed).TotalSeconds);
            throw new RateLimitedException(
                $"Vui lòng chờ {wait} giây nữa rồi gửi lại mã OTP.",
                "otp_cooldown");
        }
    }

    private void EnsureDailyQuota(string e164, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(e164, out var counter))
            return;

        if (counter.CountInWindow(now - DailyWindow) >= _options.MaxSendsPerDay)
        {
            throw new RateLimitedException(
                "Bạn đã yêu cầu quá nhiều mã OTP. Vui lòng thử lại sau 24 giờ.",
                "otp_daily_limit");
        }
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _store)
        {
            if (pair.Value.IsExpired(now))
                _store.TryRemove(pair.Key, out _);
        }

        foreach (var pair in _counters)
        {
            if (pair.Value.CountInWindow(now - DailyWindow) == 0)
                _counters.TryRemove(pair.Key, out _);
        }

        foreach (var pair in _locks)
        {
            if (now > pair.Value)
                _locks.TryRemove(pair.Key, out _);
        }
    }

    private static string RequireValidPhone(string phone)
    {
        return PhoneNormalizer.TryNormalize(phone, out var e164)
            ? e164
            : throw new ValidationException("Số điện thoại không hợp lệ.", "phone_invalid");
    }

    private static string NewCode()
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);

        var value = (ushort)(BitConverter.ToUInt32(bytes) % 900000) + 100000;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));

    public sealed class OtpEntry
    {
        public string CodeHash { get; set; } = string.Empty;
        public DateTimeOffset IssuedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public int Attempts { get; set; }
        public bool IsExpired(DateTimeOffset now) => now > ExpiresAtUtc;
    }

    private sealed class SendCounter
    {
        private readonly Queue<DateTimeOffset> _timestamps = new();

        public void Record(DateTimeOffset now)
        {
            lock (_timestamps)
            {
                _timestamps.Enqueue(now);
            }
        }

        public int CountInWindow(DateTimeOffset from)
        {
            lock (_timestamps)
            {
                while (_timestamps.Count > 0 && _timestamps.Peek() < from)
                    _timestamps.Dequeue();

                return _timestamps.Count;
            }
        }
    }
}
