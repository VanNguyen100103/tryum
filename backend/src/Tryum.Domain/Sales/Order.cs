using Tryum.Domain.Common;

namespace Tryum.Domain.Sales;

/// <summary>
/// Hóa đơn bán hàng (POS).
/// Lưu snapshot tên/SĐT khách và giá bán tại thời điểm bán để báo cáo không bị lệch
/// khi sản phẩm/khách hàng đổi thông tin về sau.
/// </summary>
public sealed class Order : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Mã hóa đơn hiển thị cho khách, ví dụ HD000123 (cũng là nội dung chuyển khoản).</summary>
    public string Code { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    public Guid? SoldByUserId { get; set; }
    public string? SoldByName { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public SaleChannel Channel { get; set; } = SaleChannel.Regular;
    public string? DeliveryAddress { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientPhone { get; set; }
    public string? DeliveryCityId { get; set; }
    public string? DeliveryDistrictId { get; set; }
    public string? DeliveryWardId { get; set; }
    public string? DeliveryCityName { get; set; }
    public string? DeliveryDistrictName { get; set; }
    public string? DeliveryWardName { get; set; }

    /// <summary>Phí vận chuyển khách trả, đã cộng vào Total.</summary>
    public decimal ShippingFee { get; set; }

    public string? ShippingCarrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? ShippingRateId { get; set; }
    public string? ShippingService { get; set; }
    public string? ShippingExpected { get; set; }
    public int? ShippingStatus { get; set; }
    public string? ShippingStatusText { get; set; }
    public string? Note { get; set; }
    public string? CouponCode { get; set; }

    /// <summary>Tổng tiền hàng trước giảm giá.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Giảm giá trên toàn hóa đơn (số tiền tuyệt đối).</summary>
    public decimal Discount { get; set; }

    /// <summary>Khách cần trả = Subtotal - Discount.</summary>
    public decimal Total { get; set; }

    /// <summary>Tổng tiền đã xác nhận thu được.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>Tiền thừa trả khách (chỉ phát sinh với tiền mặt).</summary>
    public decimal ChangeAmount { get; set; }

    /// <summary>Số điểm đã dùng để trừ tiền.</summary>
    public int PointsRedeemed { get; set; }

    /// <summary>Giá trị quy đổi của điểm đã dùng.</summary>
    public decimal PointsDiscount { get; set; }

    /// <summary>Số điểm khách được cộng sau đơn này.</summary>
    public int PointsEarned { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
