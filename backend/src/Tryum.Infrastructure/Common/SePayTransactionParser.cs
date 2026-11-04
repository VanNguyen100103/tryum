using System.Text.RegularExpressions;

namespace Tryum.Infrastructure.Common;

/// <summary>
/// Trích thông tin nghiệp vụ từ giao dịch ngân hàng.
/// Tách riêng để test được mà không cần HTTP/DB.
/// </summary>
public static partial class SePayTransactionParser
{
    /// <summary>Mã hóa đơn dạng HD000123 (4–12 chữ số) nằm trong nội dung chuyển khoản.</summary>
    [GeneratedRegex(@"\bHD\d{4,12}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderCodeRegex();

    /// <summary>Mã phiên QR tại quầy dạng POS123456789.</summary>
    [GeneratedRegex(@"\bPOS\d{6,12}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PosReferenceRegex();

    /// <summary>
    /// Tách mã hóa đơn khỏi nội dung chuyển khoản. Ngân hàng có thể thêm tiền tố
    /// ("MBVCB...", "SEVN... chuyen tien") nên chỉ cần tìm đúng mẫu HD + số.
    /// </summary>
    public static string? ExtractOrderCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = OrderCodeRegex().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    /// <summary>Tách mã phiên QR tại quầy (POS...) khỏi nội dung chuyển khoản.</summary>
    public static string? ExtractPosReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = PosReferenceRegex().Match(text);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }
}
