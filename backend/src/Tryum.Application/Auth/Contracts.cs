namespace Tryum.Application.Auth;

/// <summary>Input đăng ký cửa hàng mới: tạo tenant + tài khoản chủ (bắt buộc kèm mã OTP đã gửi tới SĐT).</summary>
public sealed record RegisterRequest(
    string ShopName,
    string OwnerName,
    string Email,
    string Password,
    string Phone,
    string OtpCode);

/// <summary>Input gửi mã OTP tới số điện thoại.</summary>
public sealed record SendOtpRequest(string Phone);

/// <summary>Kết quả gửi OTP.</summary>
/// <param name="DemoOtp">Chỉ có khi chạy chế độ demo (mô phỏng SMS) – production gộp SMS gateway thì null.</param>
public sealed record SendOtpResponse(bool Sent, int ResendAfterSeconds, string? DemoOtp);

/// <summary>Input đăng nhập.</summary>
public sealed record LoginRequest(string Email, string Password);

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
