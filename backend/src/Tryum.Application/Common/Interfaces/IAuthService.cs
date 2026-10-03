using Tryum.Application.Auth;
using Tryum.Domain.Users;

namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Nghiệp vụ đăng ký / đăng nhập / refresh token.
/// Dùng chung cho Api endpoint và test.
/// </summary>
public interface IAuthService
{
    /// <summary>Đăng ký cửa hàng mới: tạo tenant + tài khoản chủ (role Owner), trả về token.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip = null, CancellationToken cancellationToken = default);

    /// <summary>Đăng nhập bằng email + mật khẩu, trả về access token và refresh token mới.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ip = null, CancellationToken cancellationToken = default);

    /// <summary>Đổi refresh token lấy access token mới (có rotation: token cũ bị revoke).</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip = null, CancellationToken cancellationToken = default);

    /// <summary>Đăng xuất: revoke toàn bộ refresh token của user.</summary>
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Thông tin user hiện tại (từ userId đã đăng nhập).</summary>
    Task<UserInfo> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}