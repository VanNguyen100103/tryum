using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Tryum.Application.Common;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// OTP in-memory (TTL 5 phút · cooldown 30s · tối đa 5 lần đoán sai · mã 6 số crypto random).
/// Phát mã qua <see cref="ISmsSender"/>: dev/demo dùng LogSmsSender (ghi log, 0đ),
/// production dùng SmsGateSmsSender (self-host trên điện thoại Android, miễn phí).
/// Production quy mô lớn: thay store bằng Redis/DB để chia sẻ giữa nhiều instance.
/// </summary>
public sealed class OtpService(
    ISmsSender? smsSender = null,
    ILogger<OtpService>? logger = null) : IOtpService
{
    private const int OtpLength = 6;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, OtpEntry> _store = new();

    public async Task<(string Code, int ResendAfterSeconds)> IssueAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);

        var now = DateTimeOffset.UtcNow;
        var code = NewCode();

        _store[e164] = new OtpEntry
        {
            Code = code,
            IssuedAtUtc = now,
            Attempts = 0,
        };

        // Gửi mã qua SMS. Dev/demo: LogSmsSender chỉ ghi log (0đ).
        // Production: SmsGateSmsSender gửi thật qua điện thoại Android (self-host, MIỄN PHÍ).
        // Nội dung dùng ASCII (không dấu) để gói gọn 1 SMS, tránh tốn phí UCS-2.
        var message = $"Tryum: Ma OTP dang ky cua ban la {code}. Hieu luc 5 phut. Khong chia se ma nay.";

        if (smsSender is not null)
            await smsSender.SendAsync(e164, message, cancellationToken);
        else
            logger?.LogInformation(
                "OTP demo (chưa cấu hình SMS sender) cho {Phone}: {Code} (hết hạn sau {Ttl}s)",
                e164, code, (int)Ttl.TotalSeconds);

        return (code, (int)Cooldown.TotalSeconds);
    }

    public Task ConsumeAsync(string phone, string code, CancellationToken cancellationToken = default)
    {
        var e164 = RequireValidPhone(phone);

        if (!_store.TryGetValue(e164, out var entry) || entry.IsExpired(DateTimeOffset.UtcNow))
        {
            _store.TryRemove(e164, out _);
            throw new ValidationException(
                "Mã OTP chưa được gửi hoặc đã hết hạn. Vui lòng gửi lại mã mới.",
                "otp_expired");
        }

        if (entry.Code != code.Trim())
        {
            entry.Attempts++;

            if (entry.Attempts >= MaxAttempts)
            {
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

    internal sealed record OtpEntry
    {
        public required string Code { get; init; }
        public required DateTimeOffset IssuedAtUtc { get; init; }
        public int Attempts { get; set; }

        public bool IsExpired(DateTimeOffset now) => now - IssuedAtUtc > Ttl;
    }
}
