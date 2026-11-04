using Tryum.Domain.Common;

namespace Tryum.Domain.Sales;

/// <summary>
/// Một phiên QR ở quầy, độc lập với hóa đơn: dùng cho thanh toán kết hợp
/// (khách trả một phần tiền mặt, phần còn lại quét QR). SePay webhook khớp
/// theo <see cref="Reference"/> (dạng POS123456789) để báo tiền đã vào.
/// </summary>
public sealed class PosQrSession : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Nội dung chuyển khoản in trên QR, duy nhất toàn hệ thống (POS + 9 số).</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>Số tiền thực nhận qua chuyển khoản (có thể nhỏ hơn Amount nếu khách chuyển thiếu).</summary>
    public decimal? TransferredAmount { get; set; }

    public PosQrStatus Status { get; set; } = PosQrStatus.Pending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Hóa đơn đã dùng phiên này (null khi chưa chốt đơn).</summary>
    public Guid? OrderId { get; set; }
}
