using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Users;

namespace Tryum.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình map <see cref="Role"/>. Role là dữ liệu dùng chung toàn hệ thống
/// (không thuộc tenant) nên không cần TenantId / query filter.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(r => r.Code)
            .IsUnique()
            .HasDatabaseName("ix_roles_code");

        builder.Property(r => r.Description)
            .HasMaxLength(200);
    }
}