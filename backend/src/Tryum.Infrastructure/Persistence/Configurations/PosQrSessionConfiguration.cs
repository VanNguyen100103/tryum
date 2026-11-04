using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Sales;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class PosQrSessionConfiguration : IEntityTypeConfiguration<PosQrSession>
{
    public void Configure(EntityTypeBuilder<PosQrSession> builder)
    {
        builder.ToTable("pos_qr_sessions");
        builder.HasKey(session => session.Id);

        builder.Property(session => session.Reference).HasMaxLength(32).IsRequired();
        builder.Property(session => session.Amount).HasPrecision(18, 2);
        builder.Property(session => session.TransferredAmount).HasPrecision(18, 2);
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasIndex(session => session.Reference)
            .IsUnique()
            .HasDatabaseName("ux_pos_qr_sessions_reference");
        builder.HasIndex(session => new { session.TenantId, session.CreatedAt })
            .HasDatabaseName("ix_pos_qr_sessions_tenant_created");
    }
}
