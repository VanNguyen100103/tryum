namespace Tryum.Domain.Cashbook;

/// <summary>Loại phiếu thu/chi của sổ quỹ.</summary>
public enum CashflowType
{
    /// <summary>Thu tiền vào quỹ (bán hàng, khách trả nợ…).</summary>
    Income,

    /// <summary>Chi tiền ra khỏi quỹ (nhập hàng, trả lương, chi phí…).</summary>
    Expense,
}

/// <summary>Phương thức thanh toán của một giao dịch trong sổ quỹ.</summary>
public enum CashflowMethod
{
    Cash,
    Transfer,
    Card,
    Wallet,
    Other,
}
