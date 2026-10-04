using System.Globalization;
using System.Text.RegularExpressions;

namespace Tryum.Application.Common;

/// <summary>
/// Chuẩn hoá &amp; kiểm tra số điện thoại di động Việt Nam.
/// Chấp nhận "0901234567", "+84 901 234 567", "84901234567" → chuẩn hoá về "+84901234567".
/// </summary>
public static partial class PhoneNormalizer
{
    /// <summary>Thử chuẩn hoá SĐT VN. Trả về false khi không phải SĐT di động VN hợp lệ.</summary>
    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = Regex.Replace(input, @"[^\d]", string.Empty);
        if (digits.Length == 0)
            return false;

        // Rút ra phần thân 9 số (loại mã quốc gia 84 / 0084 và số 0 đầu nếu có).
        string body;
        if (digits.StartsWith("0084"))
            body = digits[4..];
        else if (digits.StartsWith("84") && digits.Length is 11 or 12)
            body = digits.Length == 11 ? digits[2..] : digits[3..];
        else if (digits.Length == 10 && digits.StartsWith('0'))
            body = digits[1..];
        else
            return false;

        if (body.Length != 9 || !VnMobileRegex().IsMatch(body))
            return false;

        e164 = $"+84{body}";
        return true;
    }

    /// <summary>Prefix SĐT di động VN: 03, 05, 07, 08, 09 (9 số sau 0).</summary>
    [GeneratedRegex(@"^(3|5|7|8|9)\d{8}$")]
    private static partial Regex VnMobileRegex();

    /// <summary>Định dạng hiển thị: +84901234567 → "0901.234.567".</summary>
    public static string FormatForDisplay(string e164)
    {
        var body = e164.StartsWith("+84") ? e164[3..] : e164;
        if (body.Length != 9)
            return e164;

        return $"0{body[..3]}.{body[3..6]}.{body[6..]}";
    }
}
