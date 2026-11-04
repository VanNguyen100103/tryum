namespace Tryum.Infrastructure.Common;

/// <summary>
/// Cấu hình tích hợp SePay: xác thực webhook + tài khoản nhận chuyển khoản để sinh mã VietQR.
/// Tất cả đều tùy chọn – nếu chưa cấu hình, POS vẫn bán được bằng tiền mặt/thẻ,
/// riêng chuyển khoản sẽ hiển thị hướng dẫn thay vì mã QR.
/// </summary>
public sealed class SePayOptions
{
    public const string SectionName = "SePay";

    /// <summary>Secret Key của webhook HMAC-SHA256 (khuyến nghị). Không commit vào git.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>API Key nếu webhook cấu hình kiểu "Apikey ..." thay vì HMAC-SHA256.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Mã BIN ngân hàng theo chuẩn VietQR, ví dụ 970436 = Vietcombank.</summary>
    public string? BankBin { get; set; }

    /// <summary>Tên ngân hàng hiển thị trên POS.</summary>
    public string? BankName { get; set; }

    /// <summary>Số tài khoản nhận tiền.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Tên chủ tài khoản (không dấu, theo đúng ngân hàng).</summary>
    public string? AccountName { get; set; }

    /// <summary>Mẫu VietQR: compact, compact2, qr_only, print.</summary>
    public string VietQrTemplate { get; set; } = "compact2";

    /// <summary>
    /// URL công khai của API (ví dụ https://tryum-api.duckdns.org).
    /// Dùng để hiển thị đúng địa chỉ webhook cần dán vào SePay.
    /// </summary>
    public string? PublicApiUrl { get; set; }

    /// <summary>Chống replay: chấp nhận timestamp lệch tối đa bao nhiêu giây.</summary>
    public int TimestampToleranceSeconds { get; set; } = 300;

    /// <summary>Đã cấu hình tài khoản nhận tiền để sinh VietQR chưa.</summary>
    public bool IsBankConfigured =>
        !string.IsNullOrWhiteSpace(BankBin) && !string.IsNullOrWhiteSpace(AccountNumber);

    /// <summary>Webhook đã được bảo vệ bằng HMAC hoặc API Key chưa.</summary>
    public bool IsWebhookSecured =>
        !string.IsNullOrWhiteSpace(WebhookSecret) || !string.IsNullOrWhiteSpace(ApiKey);
}
