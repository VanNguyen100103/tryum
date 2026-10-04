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

    /// <summary>
    /// Kiểm tra SĐT đã được dùng để đăng ký chưa. Ném ConflictException nếu đã có tài khoản.
    /// Endpoint gửi OTP gọi hàm này TRƯỚC khi phát tin để không lãng phí SMS.
    /// </summary>
    Task EnsurePhoneAvailableAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>Đăng nhập bằng email HOẶC số điện thoại + mật khẩu, trả về access token và refresh token mới.</summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ip = null, CancellationToken cancellationToken = default);

    /// <summary>Đổi refresh token lấy access token mới (có rotation: token cũ bị revoke).</summary>
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip = null, CancellationToken cancellationToken = default);

    /// <summary>Đăng xuất: revoke toàn bộ refresh token của user.</summary>
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Thông tin user hiện tại (từ userId đã đăng nhập).</summary>
    Task<UserInfo> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}