using Tryum.Domain.Common;

namespace Tryum.Domain.Users;

/// <summary>
/// Refresh token dùng để cấp lại access token mà không cần đăng nhập lại.
/// Rotation: mỗi lần refresh sẽ tạo token mới và revoke token cũ.
/// </summary>
public class RefreshToken : AuditableEntity, ITenantOwned
{
    /// <summary>
    /// Token value (random 64 bytes, base64).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm token hết hạn.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Token đã bị revoke (bị thay thế bởng token mới hoặc đăng xuất).
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Ip address / user-agent của thiết bị đăng nhập (dùng để phát hiện bất thường).
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// Foreign key đến User.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// User navigation.
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Tenant sở hữu refresh token (dùng cho global query filter).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Token còn hiệu lực không (chưa revoked và chưa hết hạn).
    /// </summary>
    public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAt;
}