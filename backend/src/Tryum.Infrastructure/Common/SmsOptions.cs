namespace Tryum.Infrastructure.Common;

/// <summary>
/// Cấu hình gửi SMS (section "Sms").
/// Provider: "Log" (dev/demo – ghi log, 0đ), "SmsGate" (self-host, miễn phí – https://sms-gate.app)
/// hoặc "Textbee" (self-host qua textbee.dev, free tier – https://textbee.dev).
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Log | SmsGate | Textbee</summary>
    public string Provider { get; set; } = "Log";

    public SmsGateOptions SmsGate { get; set; } = new();

    public TextbeeOptions Textbee { get; set; } = new();
}

/// <summary>Cấu hình "SMS Gate for Android" – biến điện thoại Android thành SMS gateway.</summary>
public sealed class SmsGateOptions
{
    /// <summary>
    /// URL gửi tin (đầy đủ).
    /// Cloud: https://api.sms-gate.app/3rdparty/v1/messages
    /// Local (cùng Wi-Fi): http://192.168.1.50:8080/message
    /// </summary>
    public string SendUrl { get; set; } = "https://api.sms-gate.app/3rdparty/v1/messages";

    /// <summary>Tài khoản Basic auth (đặt trong app SMSGate trên điện thoại).</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Chọn SIM khi máy nhiều SIM (1-based). Bỏ trống = để thiết bị tự chọn.</summary>
    public int? SimNumber { get; set; }

    /// <summary>Yêu cầu báo cáo trạng thái gửi (delivery report).</summary>
    public bool WithDeliveryReport { get; set; }

    /// <summary>Timeout HTTP (giây).</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Cấu hình "textbee.dev" – biến điện thoại Android thành SMS gateway (free tier: 1 máy,
/// 50 tin/ngày, 300 tin/tháng). API key tạo trong dashboard, gửi qua header <c>x-api-key</c>.
/// </summary>
public sealed class TextbeeOptions
{
    /// <summary>
    /// API key của textbee. Dashboard chỉ hiện 1 lần – lưu vào biến môi trường
    /// <c>TEXTBEE_API_KEY</c> (KHÔNG commit vào repo).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint gửi tin đầy đủ. Bản cloud: https://api.textbee.dev/api/v1/gateway/send-sms
    /// (đổi khi self-host: https://<domain-cua-ban>/api/v1/gateway/send-sms).
    /// </summary>
    public string SendUrl { get; set; } = "https://api.textbee.dev/api/v1/gateway/send-sms";

    /// <summary>
    /// Device ID của điện thoại gửi tin (hiển thị ở màn "You're all set!" trong app, hoặc
    /// trong dashboard). Bỏ trống = dùng máy mặc định của tài khoản.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// ID của SIM gửi tin khi máy 2 SIM (xem trong app textbee, mục SIM). Bỏ trống = SIM mặc định.
    /// </summary>
    public int? SimSubscriptionId { get; set; }

    /// <summary>Timeout HTTP (giây).</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
