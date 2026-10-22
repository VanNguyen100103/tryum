using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tryum.Domain.Products;

namespace Tryum.Infrastructure.Persistence.Configurations;

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");
        builder.HasKey(image => image.Id);
        builder.Property(image => image.SourceUrl).HasMaxLength(2048).IsRequired();
        builder.Property(image => image.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(image => image.Status).HasMaxLength(32).IsRequired();
        builder.Property(image => image.LastError).HasMaxLength(1000);
        builder.HasIndex(image => new { image.TenantId, image.ProductId });
        builder.HasIndex(image => new { image.Status, image.CreatedAt });
        builder.HasIndex(image => new { image.TenantId, image.ProductId, image.SourceUrl });
    }
}
