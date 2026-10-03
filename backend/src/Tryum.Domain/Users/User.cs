using System.Security.Claims;
using Tryum.Domain.Common;

namespace Tryum.Domain.Users;

/// <summary>
/// Người dùng hệ thống (chủ cửa hàng / nhân viên), luôn thuộc 1 tenant.
/// Mật khẩu được hash bằng BCrypt – không bao giờ lưu plain text.
/// </summary>
public class User : AuditableEntity, ITenantOwned
{
    /// <summary>
    /// Email dùng để đăng nhập (duy nhất trong hệ thống).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hash mật khẩu (format: "algorithm$salt$hash" hoặc prefix "$2b$" của BCrypt).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Họ tên người dùng.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// User đã bị khóa / nghỉ việc (không đăng nhập được nhưng dữ liệu bán hàng vẫn giữ nguyên).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Tenant sở hữu user này (dùng cho global query filter multi-tenant).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Tenant mà user này thuộc về (navigation property).
    /// </summary>
    public virtual Tenants.Tenant? Tenant { get; set; }

    /// <summary>
    /// Vai trò hiện tại của user.
    /// </summary>
    public Guid? RoleId { get; set; }

    /// <summary>
    /// Role navigation.
    /// </summary>
    public virtual Role? Role { get; set; }

    /// <summary>
    /// Danh sách refresh token đang hiệu lực.
    /// </summary>
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>
    /// Tạo claims cho JWT dựa trên thông tin user.
    /// </summary>
    public List<Claim> GetClaims()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Id.ToString()),
            new(ClaimTypes.Email, Email),
            new(ClaimTypes.Name, FullName),
            new(TenantClaimTypes.TenantId, TenantId.ToString()),
        };

        if (Role is not null)
            claims.Add(new Claim(ClaimTypes.Role, Role.Code));

        return claims;
    }
}