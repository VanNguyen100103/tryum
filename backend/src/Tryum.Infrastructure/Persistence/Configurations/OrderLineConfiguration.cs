using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Sales;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.Sku).HasMaxLength(120).IsRequired();
        builder.Property(line => line.ProductName).HasMaxLength(500).IsRequired();
        builder.Property(line => line.UnitName).HasMaxLength(40);

        builder.Property(line => line.UnitPrice).HasPrecision(18, 2);
        builder.Property(line => line.UnitCost).HasPrecision(18, 2);
        builder.Property(line => line.Quantity).HasPrecision(18, 3);
        builder.Property(line => line.Discount).HasPrecision(18, 2);
        builder.Property(line => line.LineTotal).HasPrecision(18, 2);

        builder.HasIndex(line => line.OrderId).HasDatabaseName("ix_order_lines_order");
        builder.HasIndex(line => line.ProductId).HasDatabaseName("ix_order_lines_product");
    }
}
