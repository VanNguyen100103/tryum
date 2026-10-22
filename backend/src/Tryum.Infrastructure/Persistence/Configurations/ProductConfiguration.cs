using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Products;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Sku).HasMaxLength(120).IsRequired();
        builder.Property(product => product.Barcode).HasMaxLength(120);
        builder.Property(product => product.Name).HasMaxLength(500).IsRequired();
        builder.Property(product => product.ItemType).HasMaxLength(100).IsRequired();
        builder.Property(product => product.CategoryPath).HasMaxLength(500);
        builder.Property(product => product.Brand).HasMaxLength(250);
        builder.Property(product => product.SalePrice).HasPrecision(18, 2);
        builder.Property(product => product.CostPrice).HasPrecision(18, 2);
        builder.Property(product => product.Stock).HasPrecision(18, 3);
        builder.Property(product => product.SourceImageUrlsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(product => product.R2ObjectKeysJson).HasColumnType("jsonb").IsRequired();
        builder.Property(product => product.SourceDataJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(product => new { product.TenantId, product.Sku }).IsUnique().HasDatabaseName("ix_products_tenant_sku");
        builder.HasIndex(product => new { product.TenantId, product.Barcode }).HasDatabaseName("ix_products_tenant_barcode");
        builder.HasIndex(product => new { product.TenantId, product.Name }).HasDatabaseName("ix_products_tenant_name");
        builder.HasMany(product => product.Images).WithOne(image => image.Product).HasForeignKey(image => image.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
