namespace Tryum.Domain.Common;

/// <summary>
/// Entity có audit trail: CreatedAt, UpdatedAt.
/// Hầu hết các entity trong dự án sẽ kế thừa class này.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
