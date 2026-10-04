using System.Text;
using Tryum.Application.Common;
using Tryum.Application.Common.Exceptions;
using Tryum.Application.Common.Interfaces;
using Tryum.Infrastructure.Common;
using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Auth;

public class OtpServiceTests
{
    private readonly FakeSmsSender _sms = new();
    private readonly OtpService _service;

    public OtpServiceTests()
    {
        _service = new OtpService(_sms);
    }

    [Fact]
    public async Task IssueAsync_ReturnsSixDigitCode()
    {
        var result = await _service.IssueAsync("0901234567");

        Assert.Matches(@"^\d{6}$", result.Code);
        Assert.Equal(30, result.ResendAfterSeconds);
        Assert.Equal(300, result.ExpiresInSeconds);
    }

    [Fact]
    public async Task IssueAsync_SendsCodeViaSmsSender()
    {
        var code = (await _service.IssueAsync("0901234567")).Code;

        var sent = Assert.Single(_sms.Sent);
        Assert.Equal("+84901234567", sent.Phone);
        Assert.Contains(code, sent.Message);
    }

    [Fact]
    public async Task ConsumeAsync_Succeeds_WithCorrectCode()
    {
        var code = (await _service.IssueAsync("0901234567")).Code;

        await _service.ConsumeAsync("0901234567", code);

        // Dùng lại mã cũ phải bị từ chối (mã dùng một lần).
        await Assert.ThrowsAsync<ValidationException>(async () => await _service.ConsumeAsync("0901234567", code));
    }

    [Fact]
    public async Task ConsumeAsync_NormalizesPhoneFormats()
    {
        var code = (await _service.IssueAsync("0901234567")).Code;

        await _service.ConsumeAsync("+84901234567", code);
    }

    [Fact]
    public async Task ConsumeAsync_ThrowsInvalid_WithWrongCode()
    {
        await _service.IssueAsync("0901234567");

        var ex = await Assert.ThrowsAsync<ValidationException>(
            async () => await _service.ConsumeAsync("0901234567", "000000"));

        Assert.Equal("otp_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task ConsumeAsync_ThrowsExpired_WhenNoOtpIssued()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            async () => await _service.ConsumeAsync("0901234567", "123456"));

        Assert.Equal("otp_expired", ex.ErrorCode);
    }

    [Fact]
    public async Task ConsumeAsync_LocksAfterFiveWrongAttempts()
    {
        await _service.IssueAsync("0901234567");

        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<ValidationException>(async () => await _service.ConsumeAsync("0901234567", "000000"));

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            async () => await _service.ConsumeAsync("0901234567", "000000"));

        Assert.Equal("otp_locked", ex.ErrorCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789")]
    [InlineData("8901234567")]
    [InlineData("")]
    public async Task IssueAsync_RejectsInvalidPhone(string phone)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await _service.IssueAsync(phone));

        Assert.Equal("phone_invalid", ex.ErrorCode);
    }

    [Theory]
    [InlineData("0901234567", "+84901234567")]
    [InlineData("+84 901 234 567", "+84901234567")]
    [InlineData("0084901234567", "+84901234567")]
    [InlineData("0312.345.678", "+84312345678")]
    public void TryNormalize_ProducesE164(string input, string expected)
    {
        Assert.True(PhoneNormalizer.TryNormalize(input, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0123456789")]
    [InlineData("0234567890")]
    public void TryNormalize_RejectsNonMobile(string input)
    {
        Assert.False(PhoneNormalizer.TryNormalize(input, out _));
    }

    [Fact]
    public void FormatForDisplay_FormatsVietnamStyle()
    {
        Assert.Equal("0901.234.567", PhoneNormalizer.FormatForDisplay("+84901234567"));
    }

    // ── Guards chống spam (logic giống KiotViet) ─────────────────────────────

    [Fact]
    public async Task IssueAsync_BlocksResend_DuringCooldown()
    {
        await _service.IssueAsync("0901234567");

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            async () => await _service.IssueAsync("0901234567"));

        Assert.Equal("otp_cooldown", ex.ErrorCode);

        // Bấm liên tục không được tạo thêm tin SMS nào.
        Assert.Single(_sms.Sent);
    }

    [Fact]
    public async Task IssueAsync_AllowsResend_WhenCooldownDisabled()
    {
        var service = new OtpService(_sms, options: new OtpOptions { CooldownSeconds = 0 });

        await service.IssueAsync("0901234567");
        await service.IssueAsync("0901234567");

        Assert.Equal(2, _sms.Sent.Count);
    }

    [Fact]
    public async Task IssueAsync_EnforcesDailyQuota()
    {
        // Cooldown tắt để chỉ kiểm giới hạn/ngày, quota = 3 tin.
        var service = new OtpService(
            _sms,
            options: new OtpOptions { CooldownSeconds = 0, MaxSendsPerDay = 3 });

        await service.IssueAsync("0901234567");
        await service.IssueAsync("0901234567");
        await service.IssueAsync("0901234567");

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            async () => await service.IssueAsync("0901234567"));

        Assert.Equal("otp_daily_limit", ex.ErrorCode);
        Assert.Equal(3, _sms.Sent.Count);
    }

    [Fact]
    public async Task IssueAsync_BlocksAfterTooManyWrongAttempts()
    {
        await _service.IssueAsync("0901234567");

        // 4 lần sai trả về otp_invalid; lần thứ 5 đạt ngưỡng => khoá (otp_locked).
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<ValidationException>(async () => await _service.ConsumeAsync("0901234567", "000000"));

        await Assert.ThrowsAsync<RateLimitedException>(
            async () => await _service.ConsumeAsync("0901234567", "000000"));

        // Đoán sai 5 lần => khoá, không gửi thêm tin SMS nào nữa.
        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            async () => await _service.IssueAsync("0901234567"));

        Assert.Equal("otp_locked", ex.ErrorCode);
        Assert.Single(_sms.Sent);
    }

    /// <summary>Ghi lại các SMS đã "gửi" để kiểm tra OtpService có phát tin hay không.</summary>
    private sealed class FakeSmsSender : ISmsSender
    {
        public List<(string Phone, string Message)> Sent { get; } = [];

        public Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add((phoneE164, message));
            return Task.CompletedTask;
        }
    }
}

/// <summary>Captcha server-side: sinh ảnh SVG + kiểm mã dùng 1 lần (điều kiện cần để bảo vệ quota SMS).</summary>
public class CaptchaServiceTests
{
    private readonly CaptchaService _service = new();

    [Fact]
    public void Create_ReturnsIdAndSvgImage()
    {
        var challenge = _service.Create();

        Assert.NotEmpty(challenge.CaptchaId);
        Assert.StartsWith("data:image/svg+xml;base64,", challenge.ImageDataUrl);

        var svg = DecodeSvg(challenge.ImageDataUrl);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Create_ReturnsDifferentIds()
    {
        Assert.NotEqual(_service.Create().CaptchaId, _service.Create().CaptchaId);
    }

    [Fact]
    public void Validate_RejectsWrongCode()
    {
        var challenge = _service.Create();

        Assert.False(_service.Validate(challenge.CaptchaId, "ZZZZ"));
    }

    [Fact]
    public void Validate_RejectsMissingArguments()
    {
        var challenge = _service.Create();

        Assert.False(_service.Validate(null, "ABCD"));
        Assert.False(_service.Validate(challenge.CaptchaId, null));
        Assert.False(_service.Validate("khong-ton-tai", "ABCD"));
    }

    [Fact]
    public void Validate_AcceptsCorrectCode_Once()
    {
        var challenge = _service.Create();
        var code = DecodeText(challenge.ImageDataUrl);

        Assert.True(_service.Validate(challenge.CaptchaId, code));

        // Mã dùng 1 lần: dùng lại phải bị từ chối (chống replay).
        Assert.False(_service.Validate(challenge.CaptchaId, code));
    }

    [Fact]
    public void Validate_IsCaseInsensitive()
    {
        var challenge = _service.Create();
        var code = DecodeText(challenge.ImageDataUrl);

        Assert.True(_service.Validate(challenge.CaptchaId, code.ToLowerInvariant()));
    }

    [Fact]
    public void Validate_RejectsExpiredChallenge()
    {
        // TTL âm => mã hết hạn ngay lập tức.
        var service = new CaptchaService(new CaptchaOptions { TtlSeconds = -1 });
        var challenge = service.Create();

        Assert.False(service.Validate(challenge.CaptchaId, DecodeText(challenge.ImageDataUrl)));
    }

    /// <summary>Giải mã data-uri để đọc nội dung SVG.</summary>
    private static string DecodeSvg(string dataUrl)
    {
        var payload = dataUrl["data:image/svg+xml;base64,".Length..];
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    /// <summary>Bóc 4 ký tự trong thẻ &lt;text&gt; của SVG (dùng để mô phỏng người dùng nhìn đúng).</summary>
    private static string DecodeText(string dataUrl)
    {
        var svg = DecodeSvg(dataUrl);

        var matches = System.Text.RegularExpressions.Regex.Matches(svg, ">([A-Z0-9])</text>");

        return string.Concat(matches.Select(m => m.Groups[1].Value));
    }
}
