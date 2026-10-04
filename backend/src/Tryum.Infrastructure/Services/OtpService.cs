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
/// OTP in-memory: TTL + cooldown + giới hạn số lần đoán sai + giới hạn số tin/ngày mỗi SĐT,
/// mã 6 số sinh bằng crypto random và chỉ lưu SHA-256 trong bộ nhớ.
/// Phát mã qua <see cref="ISmsSender"/>: dev/demo dùng LogSmsSender (ghi log, 0đ),
/// production dùng SmsGateSmsSender hoặc TextbeeSmsSender (self-host trên điện thoại Android, MIỄN PHÍ).
/// Vì mỗi lần IssueAsync đều tốn 1 tin SMS, tầng API bắt buộc kiểm captcha server-side trước.
/// Production quy mô lớn: thay store bằng Redis/DB để chia sẻ giữa nhiều instance.
/// </summary>
public sealed class OtpService(
    ISmsSender? smsSender = null,
    ILogger<OtpService>? logger = null,
    OtpOptions? options = null) : IOtpService
{
    private static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);

    private readonly OtpOptions _options = options ?? new OtpOptions();
    private readonly ConcurrentDictionary<string, OtpEntry> _store = new();
    private readonly ConcurrentDictionary<string, SendCounter> _counters = new();

    /// <summary>Số điện thoại đang bị khoá do đoán sai quá nhiều lần.</summary>
    private readonly ConcurrentDictionary<string, LockState> _locks = new();

    public async Task<OtpIssueResult> IssueAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);
        var now = DateTimeOffset.UtcNow;

        PurgeExpired();

        // Thứ tự có chủ ý: báo "khoá do đoán sai" trước, rồi mới báo cooldown,
        // để thông báo khớp với nguyên nhân thật.
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

        // Gửi mã qua SMS. Dev/demo: LogSmsSender chỉ ghi log (0đ).
        // Production: SmsGateSmsSender / TextbeeSmsSender gửi thật qua điện thoại Android.
        // Nội dung dùng ASCII (không dấu) để gói gọn 1 SMS, tránh tốn phí UCS-2.
        var message = $"Tryum: Ma OTP dang ky cua ban la {code}. " +
                      $"Hieu luc {_options.TtlSeconds / 60} phut. Khong chia se ma nay.";

        if (smsSender is not null)
            await smsSender.SendAsync(e164, message, cancellationToken);
        else
            logger?.LogInformation(
                "OTP demo (chưa cấu hình SMS sender) cho {Phone}: {Code} (hết hạn sau {Ttl}s)",
                e164, code, _options.TtlSeconds);

        _counters.GetOrAdd(e164, _ => new SendCounter()).Record(now);

        return new OtpIssueResult(code, _options.CooldownSeconds, _options.TtlSeconds);
    }

    /// <summary>SĐT đang có mã OTP chờ xác nhận hay không (chưa hết hạn).</summary>
    public bool HasPendingCode(string phone)
    {
        if (!PhoneNormalizer.TryNormalize(phone, out var e164))
            return false;

        return _store.TryGetValue(e164, out var entry)
               && !entry.IsExpired(DateTimeOffset.UtcNow);
    }

    public Task ConsumeAsync(string phone, string code, CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);
        var now = DateTimeOffset.UtcNow;

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
                // Giữ dấu vết trạng thái khoá (số lần sai + mốc hết hạn) ở store riêng
                // để lần gửi tiếp theo báo đúng nguyên nhân "sai quá nhiều lần".
                _locks[e164] = new LockState(now.AddSeconds(_options.TtlSeconds));

                _store.TryRemove(e164, out _);
                throw new RateLimitedException(
                    "Quá số lần thử sai. Vui lòng gửi mã OTP mới.",
                    "otp_locked");
            }

            throw new ValidationException("Mã OTP không đúng.", "otp_invalid");
        }

        _store.TryRemove(e164, out _);
        return Task.CompletedTask;
    }

    /// <summary>Đối soát khi muốn kiểm tra thử (test).</summary>
    internal OtpEntry? Peek(string phone) =>
        PhoneNormalizer.TryNormalize(phone, out var e164) && _store.TryGetValue(e164, out var entry)
            ? entry
            : null;

    // ── Guards chống spam ───────────────────────────────────────────────────

    /// <summary>Đoán sai quá nhiều lần thì khoá tới khi mốc hết hạn đi qua.</summary>
    private void EnsureNotLockedOut(string e164, DateTimeOffset now)
    {
        if (_locks.TryGetValue(e164, out var lockState))
        {
            if (now <= lockState.UntilUtc)
                throw new RateLimitedException(
                    "Bạn đã nhập sai mã quá nhiều lần. Vui lòng gửi mã OTP mới sau ít phút.",
                    "otp_locked");

            _locks.TryRemove(e164, out _);
        }
    }

    /// <summary>Chặn bấm "gửi lại" liên tục trong khoảng thời gian chờ (giống KiotViet).</summary>
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

    /// <summary>Giới hạn số tin SMS mỗi SĐT trong 24 giờ – bảo vệ quota gateway.</summary>
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

    /// <summary>Dọn mã hết hạn và bộ đếm cũ để store không phình vô hạn.</summary>
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
            if (now > pair.Value.UntilUtc)
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

        // 100000..999999 (đúng 6 chữ số).
        var value = (ushort)(BitConverter.ToUInt32(bytes) % 900000) + 100000;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));

    internal sealed record OtpEntry
    {
        /// <summary>SHA-256 của mã OTP – không lưu mã rõ trong bộ nhớ.</summary>
        public required string CodeHash { get; init; }

        public required DateTimeOffset IssuedAtUtc { get; init; }

        public required DateTimeOffset ExpiresAtUtc { get; init; }

        public int Attempts { get; set; }

        public bool IsExpired(DateTimeOffset now) => now > ExpiresAtUtc;
    }

    /// <summary>Trạng thái khoá tạm thời sau khi đoán sai quá nhiều lần.</summary>
    private sealed record LockState(DateTimeOffset UntilUtc);

    /// <summary>Bộ đếm số tin đã gửi trong cửa sổ 24 giờ (thread-safe).</summary>
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
