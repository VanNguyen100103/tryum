namespace Tryum.Domain.Users;

/// <summary>
/// Các mã vai trò (khớp với <see cref="Role.Code"/> trong DB).
/// </summary>
public enum RoleCode
{
    /// <summary>
    /// Chủ cửa hàng – toàn quyền trong tenant (kể cả xoá dữ liệu, quản lý nhân viên).
    /// </summary>
    Owner = 1,

    /// <summary>
    /// Quản lý – xem báo cáo, quản lý hàng hóa/kho; không quản lý nhân viên.
    /// </summary>
    Manager = 2,

    /// <summary>
    /// Thu ngân – bán hàng, hoàn hóa đơn, xem lịch sử khách hàng.
    /// </summary>
    Cashier = 3,
}