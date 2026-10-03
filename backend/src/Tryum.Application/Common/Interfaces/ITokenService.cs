using Tryum.Domain.Users;

namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Service tạo JWT access token và quản lý refresh token.
/// </summary>
public interface ITokenService
{
    /// <summary>Thời hạn access token (phút) – lấy từ cấu hình Jwt.</summary>
    int AccessTokenLifetimeMinutes { get; }

    /// <summary>Tạo JWT access token chứa userId, email, tenantId, role.</summary>
    string GenerateAccessToken(User user);

    /// <summary>Tạo refresh token mới cho user và lưu vào DB (kèm tenantId).</summary>
    Task<RefreshToken> GenerateRefreshTokenAsync(
        Guid userId,
        Guid tenantId,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm refresh token còn hiệu lực theo giá trị token (bỏ query filter vì chưa biết tenant).
    /// </summary>
    Task<RefreshToken?> GetActiveRefreshTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>Rotation: revoke token cũ, tạo token mới cùng user/tenant.</summary>
    Task<RefreshToken> RotateRefreshTokenAsync(
        RefreshToken oldToken,
        string? createdByIp,
        CancellationToken cancellationToken = default);

    /// <summary>Hủy tất cả refresh token của user (đăng xuất mọi thiết bị).</summary>
    Task RevokeAllRefreshTokensAsync(
        Guid userId,
        string? createdByIp,
        CancellationToken cancellationToken = default);
}