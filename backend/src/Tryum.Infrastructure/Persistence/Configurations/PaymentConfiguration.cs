using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Sales;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(payment => payment.Amount).HasPrecision(18, 2);
        builder.Property(payment => payment.Provider).HasMaxLength(40);
        builder.Property(payment => payment.ProviderTransactionId).HasMaxLength(120);
        builder.Property(payment => payment.Reference).HasMaxLength(300);
        builder.Property(payment => payment.Note).HasMaxLength(500);

        builder.HasIndex(payment => payment.OrderId).HasDatabaseName("ix_payments_order");
        builder.HasIndex(payment => new { payment.Provider, payment.ProviderTransactionId })
            .HasDatabaseName("ix_payments_provider_tx");
    }
}
