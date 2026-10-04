namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Phát hành &amp; xác thực mã OTP cho số điện thoại (chống spam, brute force).
/// Demo mode: chỉ "phát hành" vào bộ nhớ, chưa gọi SMS gateway thật.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Phát hành mã OTP 6 số cho SĐT (chuẩn hoá E.164).
    /// Trùng lặp trong cooldown → RateLimitedException; SĐT sai → ValidationException.
    /// </summary>
    Task<(string Code, int ResendAfterSeconds)> IssueAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác thực mã: đúng → consume (xoá mã, không dùng lại được).
    /// Sai / hết hạn / quá số lần thử → ValidationException (hoặc RateLimitedException khi bị khoá).
    /// </summary>
    Task ConsumeAsync(string phone, string code, CancellationToken cancellationToken = default);
}
