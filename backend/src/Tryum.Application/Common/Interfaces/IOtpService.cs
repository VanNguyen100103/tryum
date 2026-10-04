namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Phát hành &amp; xác thực mã OTP cho số điện thoại (chống spam, brute force).
/// Mỗi lần gửi đều đi qua <see cref="ISmsSender"/> nên đòi captcha server-side ở tầng API
/// trước khi gọi vào đây (giống KiotViet) – nếu không, bot sẽ đốt hết quota SMS.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Phát hành mã OTP 6 số cho SĐT (chuẩn hoá E.164).
    /// - SĐT sai → <c>ValidationException("phone_invalid")</c>.
    /// - Gửi lại trong cooldown hoặc vượt giới hạn/ngày → <c>RateLimitedException</c>.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra SĐT đang có mã OTP chờ (chưa hết hạn, chưa dùng) hay không.
    /// Dùng để cho phép "gửi lại" mà không cần captcha mới – CHỉ đúng khi mã đó do
    /// chính người dùng vừa yêu cầu (đã vượt captcha ở lần gửi đầu).
    /// </summary>
    bool HasPendingCode(string phone);

    /// <summary>
    /// Xác thực mã: đúng → consume (xoá mã, không dùng lại được).
    /// Sai / hết hạn / quá số lần thử → ValidationException (hoặc RateLimitedException khi bị khoá).
    /// </summary>
    Task ConsumeAsync(string phone, string code, CancellationToken cancellationToken = default);
}

/// <summary>Kết quả phát hành OTP: mã (chỉ dev dùng), số giây chờ gửi lại, số giây còn hiệu lực.</summary>
public sealed record OtpIssueResult(string Code, int ResendAfterSeconds, int ExpiresInSeconds);
