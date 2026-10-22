using Tryum.Domain.Common;

namespace Tryum.Domain.Products;

public sealed class ProductImage : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Status { get; set; } = "Pending";
    public string? LastError { get; set; }
    public Product? Product { get; set; }
}
