using Tryum.Domain.Common;

namespace Tryum.Domain.Sales;

/// <summary>
/// Nhật ký webhook đã nhận (không thuộc tenant nào – dùng để chống xử lý trùng).
/// UNIQUE(Provider, EventId) đảm bảo mỗi giao dịch ngân hàng chỉ được cộng tiền một lần,
/// kể cả khi SePay retry hoặc admin bấm "gửi lại" nhiều lần.
/// </summary>
public sealed class PaymentWebhookEvent : BaseEntity
{
    public string Provider { get; set; } = string.Empty;

    /// <summary>ID giao dịch bên gửi webhook (SePay: trường <c>id</c>).</summary>
    public string EventId { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }
    public string PayloadJson { get; set; } = "{}";
}
