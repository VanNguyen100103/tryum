namespace Tryum.Infrastructure.Common;

/// <summary>
/// Cấu hình luồng OTP (section "Otp"): hạn mã, thời gian chờ giữa 2 lần gửi,
/// số lần đoán sai tối đa và số tin SMS tối đa mỗi SĐT trong 24 giờ.
/// Đây là các "quy tắc chống spam" — đặt số khác so với mặc định sẽ nới/nặng thêm.
/// </summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Mã OTP hết hạn sau bao nhiêu giây (mặc định 300 = 5 phút).</summary>
    public int TtlSeconds { get; set; } = 300;

    /// <summary>Chờ tối thiểu bao nhiêu giây giữa 2 lần gửi liên tiếp cùng một SĐT (mặc định 30).</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>Số lần nhập sai mã tối đa trước khi khoá (mặc định 5).</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Số tin OTP tối đa cho một SĐT trong 24 giờ (mặc định 5).
    /// Đây là hàng rào bảo vệ quota SMS gateway (textbee free tier chỉ 50 tin/ngày).
    /// </summary>
    public int MaxSendsPerDay { get; set; } = 5;
}