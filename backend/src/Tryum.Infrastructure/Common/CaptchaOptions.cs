namespace Tryum.Infrastructure.Common;

/// <summary>Cấu hình captcha (section "Captcha"): thời gian sống của một mã và có bắt buộc không.</summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    /// <summary>Mã captcha hết hạn sau bao nhiêu giây (mặc định 300 = 5 phút).</summary>
    public int TtlSeconds { get; set; } = 300;

    /// <summary>
    /// Bắt buộc kiểm captcha khi gửi OTP hay không (mặc định true).
    /// Chỉ tắt khi test tự động / môi trường demo nội bộ.
    /// </summary>
    public bool Required { get; set; } = true;
}