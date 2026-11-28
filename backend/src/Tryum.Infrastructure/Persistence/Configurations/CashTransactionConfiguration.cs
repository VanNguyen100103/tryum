using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Cashbook;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.ToTable("cash_transactions");
        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Code).HasMaxLength(32).IsRequired();
        builder.Property(transaction => transaction.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(transaction => transaction.Method).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(18, 2);
        builder.Property(transaction => transaction.Category).HasMaxLength(160);
        builder.Property(transaction => transaction.PartnerName).HasMaxLength(200);
        builder.Property(transaction => transaction.ReferenceCode).HasMaxLength(60);
        builder.Property(transaction => transaction.Note).HasMaxLength(1000);
        builder.Property(transaction => transaction.CreatedByName).HasMaxLength(200);

        builder.HasIndex(transaction => new { transaction.TenantId, transaction.Code }).IsUnique();
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.TransactedAt });
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.Type });
    }
}
