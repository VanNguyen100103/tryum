using System.Security.Claims;

namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Truy xuất thông tin user hiện tại từ HTTP context (claims).
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Guid của user hiện tại (null nếu chưa đăng nhập).
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Email của user hiện tại.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Guid tenant hiện tại (null nếu là super admin hoặc chưa chọn tenant).
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Mã vai trò (RoleCode) của user hiện tại.
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// Kiểm tra user có claim role cụ thể không.
    /// </summary>
    bool IsInRole(string roleCode);

    /// <summary>
    /// Lấy toàn bộ claims của user hiện tại.
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// TenantId hiện tại, ném lỗi nếu chưa đăng nhập (endpoint bắt buộc có tenant).
    /// </summary>
    Guid RequireTenantId();

    /// <summary>
    /// UserId hiện tại, ném lỗi nếu chưa đăng nhập.
    /// </summary>
    Guid RequireUserId();
}