using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Tenants;

namespace Tryum.Infrastructure.Persistence.Configurations;

/// <summary>Cấu hình map <see cref="Tenant"/>: slug duy nhất (dùng làm subdomain).</summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Slug)
            .HasMaxLength(63)
            .IsRequired();

        builder.HasIndex(t => t.Slug)
            .IsUnique()
            .HasDatabaseName("ix_tenants_slug");

        builder.Property(t => t.StoreCode)
            .HasMaxLength(50);

        builder.Property(t => t.Phone)
            .HasMaxLength(20);

        builder.Property(t => t.Address)
            .HasMaxLength(500);
    }
}