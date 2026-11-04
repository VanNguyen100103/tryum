using Tryum.Domain.Common;

namespace Tryum.Domain.Products;

public sealed class Product : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string? CategoryPath { get; set; }
    public string? Brand { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? Stock { get; set; }
    public decimal? MinStock { get; set; }
    public decimal? MaxStock { get; set; }
    public string? Position { get; set; }
    public decimal? Weight { get; set; }
    public string? Description { get; set; }
    public bool IsDirectSale { get; set; } = true;
    public string UnitsJson { get; set; } = "[]";
    public string AttributesJson { get; set; } = "[]";
    public string VariantsJson { get; set; } = "[]";
    public string SourceImageUrlsJson { get; set; } = "[]";
    public string R2ObjectKeysJson { get; set; } = "[]";
    public string SourceDataJson { get; set; } = "{}";
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
