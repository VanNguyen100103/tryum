using System.Globalization;
using Tryum.Infrastructure.Common;

namespace Tryum.Api.Endpoints;

/// <summary>Sinh URL ảnh VietQR (img.vietqr.io) cho một số tiền + nội dung chuyển khoản.</summary>
public static class VietQrUrlBuilder
{
    public static string Build(SePayOptions options, decimal amount, string reference)
    {
        var template = string.IsNullOrWhiteSpace(options.VietQrTemplate) ? "compact2" : options.VietQrTemplate;
        var amountText = amount.ToString("0", CultureInfo.InvariantCulture);
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(options.BankBin!)}-" +
            $"{Uri.EscapeDataString(options.AccountNumber!)}-{Uri.EscapeDataString(template)}.svg" +
            $"?amount={amountText}" +
            $"&addInfo={Uri.EscapeDataString(reference)}" +
            $"&accountName={Uri.EscapeDataString(options.AccountName ?? string.Empty)}";
    }
}
