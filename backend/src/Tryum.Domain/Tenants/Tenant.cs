using Tryum.Domain.Common;

namespace Tryum.Domain.Tenants;

/// <summary>
/// Represents a tenant (store/company) in the multi-tenant SaaS.
/// Tenant isolation is enforced via TenantId on every entity + EF Core global query filter.
/// </summary>
public class Tenant : AuditableEntity
{
    /// <summary>
    /// Tên cửa hàng / công ty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Slug dùng cho URL (duy nhất, không dấu, không khoảng trắng).
    /// Ví dụ: "pho-thao" => https://pho-thao.tryum.app
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh ngắn (ví dụ: MÃ CỬA HÀNG) dùng trong hóa đơn.
    /// </summary>
    public string? StoreCode { get; set; }

    /// <summary>
    /// Số điện thoại cửa hàng.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Địa chỉ cửa hàng.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Tenant có đang hoạt động không (soft delete / suspend).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Danh sách người dùng thuộc tenant này (navigation).
    /// </summary>
    public virtual ICollection<Users.User> Users { get; set; } = new List<Users.User>();
}