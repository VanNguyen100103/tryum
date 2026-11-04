using Tryum.Domain.Common;

namespace Tryum.Domain.Customers;

public sealed class Customer : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Region { get; set; }
    public string? Ward { get; set; }
    public string? GroupName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Gender { get; set; }
    public string? Facebook { get; set; }
    public string? AssignedTo { get; set; }
    public string? Note { get; set; }
    public string? CompanyName { get; set; }
    public string? TaxCode { get; set; }
    public string? InvoiceAddress { get; set; }

    /// <summary>Điểm tích lũy hiện có (1 điểm = 1.000đ khi đổi).</summary>
    public int Points { get; set; }

    /// <summary>Công nợ: dương = khách còn nợ cửa hàng, âm = cửa hàng còn nợ khách (tiền thừa gửi).</summary>
    public decimal DebtAmount { get; set; }
}
