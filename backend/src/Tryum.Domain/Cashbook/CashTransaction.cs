using Tryum.Domain.Common;

namespace Tryum.Domain.Cashbook;

/// <summary>
/// Một giao dịch thu/chi trong sổ quỹ của cửa hàng.
/// Số dư quỹ hiện tại = tổng thu (Income) - tổng chi (Expense).
/// </summary>
public sealed class CashTransaction : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>Mã phiếu hiển thị, ví dụ PT000123 (thu) hoặc PC000456 (chi).</summary>
    public string Code { get; set; } = string.Empty;

    public CashflowType Type { get; set; } = CashflowType.Income;

    public CashflowMethod Method { get; set; } = CashflowMethod.Cash;

    /// <summary>Số tiền (luôn dương); ý nghĩa thu/chi do <see cref="Type"/> quyết định.</summary>
    public decimal Amount { get; set; }

    /// <summary>Nhóm thu/chi, ví dụ: Bán hàng, Nhập hàng, Lương, Điện nước…</summary>
    public string? Category { get; set; }

    /// <summary>Đối tượng liên quan: khách hàng, nhà cung cấp, nhân viên…</summary>
    public string? PartnerName { get; set; }

    /// <summary>Tham chiếu nghiệp vụ, ví dụ mã hóa đơn HD000123.</summary>
    public string? ReferenceCode { get; set; }

    public string? Note { get; set; }

    /// <summary>Thời điểm phát sinh nghiệp vụ (khác với CreatedAt là lúc ghi sổ).</summary>
    public DateTimeOffset TransactedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
}
