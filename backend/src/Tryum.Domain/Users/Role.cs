using Tryum.Domain.Common;

namespace Tryum.Domain.Users;

/// <summary>
/// Vai trò trong hệ thống (Chủ cửa hàng / Quản lý / Thu ngân).
/// Permissions được định nghĩa ở application layer (policy-based).
/// </summary>
public class Role : AuditableEntity
{
    /// <summary>
    /// Tên vai trò hiển thị.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh (ví dụ: "OWNER", "MANAGER", "CASHIER") dùng làm claim.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả vai trò.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Danh sách user có vai trò này.
    /// </summary>
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}