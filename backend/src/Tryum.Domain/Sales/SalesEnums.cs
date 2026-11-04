namespace Tryum.Domain.Sales;

/// <summary>Trạng thái hóa đơn bán hàng.</summary>
public enum OrderStatus
{
    /// <summary>Chưa thu được đồng nào (ví dụ chờ chuyển khoản).</summary>
    Pending,

    /// <summary>Đã thu một phần, khách còn nợ.</summary>
    PartiallyPaid,

    /// <summary>Đã thu đủ tiền.</summary>
    Completed,

    /// <summary>Hóa đơn bị hủy (đã hoàn kho).</summary>
    Cancelled,
}

/// <summary>Kênh bán – tương ứng 3 chế độ trên màn hình POS (Bán nhanh / Bán thường / Bán giao hàng).</summary>
public enum SaleChannel
{
    Quick,
    Regular,
    Delivery,
}

/// <summary>Phương thức thanh toán – hỗ trợ thanh toán nhiều phương thức trong cùng một hóa đơn.</summary>
public enum PaymentMethod
{
    Cash,
    Transfer,
    Card,
    Wallet,
    Voucher,
}

/// <summary>Trạng thái một lần thu tiền.</summary>
public enum PaymentStatus
{
    /// <summary>Đang chờ xác nhận (ví dụ chuyển khoản chờ webhook SePay).</summary>
    Pending,

    /// <summary>Đã xác nhận tiền vào.</summary>
    Confirmed,

    /// <summary>Bị hủy cùng hóa đơn.</summary>
    Cancelled,
}

/// <summary>Trạng thái phiên QR tại quầy (độc lập hóa đơn).</summary>
public enum PosQrStatus
{
    /// <summary>Đang chờ khách chuyển khoản.</summary>
    Pending,

    /// <summary>Đã nhận đủ tiền.</summary>
    Paid,

    /// <summary>Hết hạn trước khi khách chuyển.</summary>
    Expired,

    /// <summary>Đã gắn vào một hóa đơn.</summary>
    Used,
}
