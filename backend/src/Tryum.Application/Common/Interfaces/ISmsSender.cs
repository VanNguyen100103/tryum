namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Gửi tin nhắn SMS tới một số điện thoại (đã chuẩn hoá E.164, ví dụ +84901234567).
/// Triển khai miễn phí: <c>LogSmsSender</c> (dev/demo – chỉ ghi log, 0đ),
/// <c>SmsGateSmsSender</c> (self-host, dùng điện thoại Android làm gateway – https://sms-gate.app)
/// hoặc <c>TextbeeSmsSender</c> (self-host, điện thoại Android làm gateway – https://textbee.dev).
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Gửi một tin SMS. Ném <c>AppException</c> (mã "sms_send_failed") khi gateway từ chối gửi
    /// để API trả về lỗi rõ ràng cho client.
    /// </summary>
    Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default);
}
