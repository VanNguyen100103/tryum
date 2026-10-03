namespace Tryum.Domain.Common;

/// <summary>
/// Tên claim dùng trong JWT. Gom vào 1 chỗ để Domain (tạo claim) và
/// Infrastructure (đọc claim) không lệch nhau.
/// </summary>
public static class TenantClaimTypes
{
    /// <summary>TenantId của user hiện tại (string Guid).</summary>
    public const string TenantId = "tenant_id";
}