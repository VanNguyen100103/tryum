using Tryum.Domain.Common;

namespace Tryum.Domain.Sales;

/// <summary>
/// Một lần thu tiền của hóa đơn. Một hóa đơn có thể có nhiều Payment
/// (thanh toán nhiều phương thức: tiền mặt + chuyển khoản + ví…).
/// </summary>
public sealed class Payment : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }

    /// <summary>Đơn vị xác nhận giao dịch (ví dụ SePay) – null với tiền mặt/thu tay.</summary>
    public string? Provider { get; set; }

    /// <summary>ID giao dịch bên thứ ba (SePay transaction id / mã tham chiếu ngân hàng).</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>Nội dung/mã tham chiếu của giao dịch chuyển khoản.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? PaidAt { get; set; }
    public string? Note { get; set; }
}
