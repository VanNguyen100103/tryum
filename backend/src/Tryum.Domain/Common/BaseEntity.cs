namespace Tryum.Domain.Common;

/// <summary>
/// Base class cho tất cả entity trong hệ thống.
/// Dùng Guid làm PK để hỗ trợ multi-tenant và phân tán sau này.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}
