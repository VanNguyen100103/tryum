namespace Tryum.Application.Auth;

/// <summary>Input đăng ký cửa hàng mới: tạo tenant + tài khoản chủ.</summary>
public sealed record RegisterRequest(
    string ShopName,
    string OwnerName,
    string Email,
    string Password,
    string? Phone);

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