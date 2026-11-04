using Tryum.Domain.Common;

namespace Tryum.Domain.Sales;

/// <summary>Một dòng hàng trong hóa đơn, kèm snapshot giá vốn để tính lãi gộp.</summary>
public sealed class OrderLine : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? UnitName { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Giảm giá trên dòng hàng (số tiền tuyệt đối).</summary>
    public decimal Discount { get; set; }

    /// <summary>Thành tiền = UnitPrice * Quantity - Discount.</summary>
    public decimal LineTotal { get; set; }
}
