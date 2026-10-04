namespace Tryum.Application.Auth;

/// <summary>Input đăng ký cửa hàng mới: tạo tenant + tài khoản chủ (bắt buộc kèm mã OTP đã gửi tới SĐT).</summary>
public sealed record RegisterRequest(
    string ShopName,
    string OwnerName,
    string Email,
    string Password,
    string Phone,
    string OtpCode);

/// <summary>
/// Input gửi mã OTP tới số điện thoại.
/// Kèm captcha server-side (chống bot đốt SMS). <paramref name="IsResend"/> = true khi người
/// dùng đang ở bước nhập OTP và bấm "Gửi lại": đây là hành động có chủ ý sau khi đã vượt captcha,
/// và vẫn bị giới hạn bởi cooldown + quota/ngày nên không cần captcha mới.
/// </summary>
public sealed record SendOtpRequest(
    string Phone,
    string? CaptchaId = null,
    string? CaptchaCode = null,
    bool IsResend = false);

/// <summary>Ảnh captcha do server sinh: id + data-uri để hiển thị, không cần thư viện ảnh.</summary>
public sealed record CaptchaResponse(string CaptchaId, string ImageDataUrl);

/// <summary>
/// Input đăng nhập. <paramref name="Identifier"/> nhận email HOẶC số điện thoại
/// (KiotViet cho đăng nhập bằng SĐT; Tryum hỗ trợ cả hai).
/// </summary>
public sealed record LoginRequest(string Identifier, string Password);

/// <summary>Kết quả gửi OTP.</summary>
/// <param name="DemoOtp">Chỉ có khi chạy chế độ demo (mô phỏng SMS) – production gộp SMS gateway thì null.</param>
/// <param name="ExpiresInSeconds">Hạn sử dụng của mã (để UI hiển thị "Mã có hiệu lực trong N phút").</param>
public sealed record SendOtpResponse(
    bool Sent,
    int ResendAfterSeconds,
    string? DemoOtp,
    int ExpiresInSeconds);

/// <summary>Input đổi access token bằng refresh token.</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Thông tin user trả về cho frontend (kèm token).</summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    UserInfo User);

/// <summary>Thông tin user hiện tại.</summary>
public sealed record UserInfo(Guid Id, string FullName, string Email, string? Phone, Guid TenantId, string TenantName, string RoleCode, string RoleName);
