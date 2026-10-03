using Tryum.Domain.Common;

namespace Tryum.Domain.Common;

/// <summary>
/// Đánh dấu entity thuộc về 1 tenant cụ thể.
/// EF Core global query filter tự động lọc theo tenant hiện tại.
/// Entity implement interface này BẮT BUỘC phải có TenantId.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}