namespace Tryum.Infrastructure.Common;

/// <summary>
/// Cấu hình gửi SMS (section "Sms").
/// Provider: "Log" (dev/demo – ghi log, 0đ) hoặc "SmsGate" (self-host, miễn phí – https://sms-gate.app).
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Log | SmsGate</summary>
    public string Provider { get; set; } = "Log";

    public SmsGateOptions SmsGate { get; set; } = new();
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
