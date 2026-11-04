using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Sales;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.Code).HasMaxLength(32).IsRequired();
        builder.Property(order => order.CustomerName).HasMaxLength(200);
        builder.Property(order => order.CustomerPhone).HasMaxLength(40);
        builder.Property(order => order.SoldByName).HasMaxLength(200);
        builder.Property(order => order.DeliveryAddress).HasMaxLength(500);
        builder.Property(order => order.Note).HasMaxLength(1000);
        builder.Property(order => order.CouponCode).HasMaxLength(60);

        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(order => order.Channel).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.Property(order => order.Subtotal).HasPrecision(18, 2);
        builder.Property(order => order.Discount).HasPrecision(18, 2);
        builder.Property(order => order.Total).HasPrecision(18, 2);
        builder.Property(order => order.PaidAmount).HasPrecision(18, 2);
        builder.Property(order => order.ChangeAmount).HasPrecision(18, 2);
        builder.Property(order => order.PointsDiscount).HasPrecision(18, 2);

        builder.HasIndex(order => new { order.TenantId, order.Code })
            .IsUnique()
            .HasDatabaseName("ix_orders_tenant_code");
        builder.HasIndex(order => new { order.TenantId, order.CreatedAt })
            .HasDatabaseName("ix_orders_tenant_created");
        builder.HasIndex(order => new { order.TenantId, order.CustomerId })
            .HasDatabaseName("ix_orders_tenant_customer");

        builder.HasMany(order => order.Lines)
            .WithOne(line => line.Order)
            .HasForeignKey(line => line.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(order => order.Payments)
            .WithOne(payment => payment.Order)
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
