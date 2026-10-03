namespace Tryum.Application.Common.Security;

/// <summary>
/// Tên các authorization policy dùng policy-based.
/// - <see cref="OwnerOrManager"/>: quản lý cửa hàng (không có Cashier).
/// - <see cref="Authenticated"/>: mọi user đã đăng nhập (kể cả thu ngân).
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Yêu cầu đăng nhập – mặc định của các API nghiệp vụ.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Chủ cửa hàng hoặc quản lý: cấu hình dữ liệu, quản lý nhân viên, báo cáo.</summary>
    public const string OwnerOrManager = "OwnerOrManager";
}