namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Sinh &amp; kiểm tra captcha phía SERVER cho form đăng ký.
/// Mục tiêu giống KiotViet: chặn bot gọi thẳng API để đốt hết quota SMS.
/// Mã dùng một lần và tự hết hạn sau vài phút.
/// </summary>
public interface ICaptchaService
{
    /// <summary>Sinh challenge mới: captchaId + ảnh SVG (data-uri) để frontend hiển thị.</summary>
    CaptchaChallenge Create();

    /// <summary>
    /// Kiểm tra mã người dùng nhập. Đúng → true và challenge bị xoá (dùng đúng 1 lần).
    /// Sai/hết hạn/không tồn tại → false.
    /// </summary>
    bool Validate(string? captchaId, string? code);
}

/// <summary>Kết quả sinh captcha: id để gửi kèm khi đăng ký + ảnh data-uri để render.</summary>
public sealed record CaptchaChallenge(string CaptchaId, string ImageDataUrl);