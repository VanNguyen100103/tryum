using Tryum.Application.Common;
using Tryum.Application.Common.Exceptions;
using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Auth;

public class OtpServiceTests
{
    private readonly OtpService _service = new();

    [Fact]
    public async Task IssueAsync_ReturnsSixDigitCode()
    {
        var (code, resendAfter) = await _service.IssueAsync("0901234567");

        Assert.Matches(@"^\d{6}$", code);
        Assert.Equal(30, resendAfter);
    }

    [Fact]
    public async Task ConsumeAsync_Succeeds_WithCorrectCode()
    {
        var (code, _) = await _service.IssueAsync("0901234567");

        await _service.ConsumeAsync("0901234567", code);

        // Dùng lại mã cũ phải bị từ chối (mã dùng một lần).
        await Assert.ThrowsAsync<ValidationException>(async () => await _service.ConsumeAsync("0901234567", code));
    }

    [Fact]
    public async Task ConsumeAsync_NormalizesPhoneFormats()
    {
        var (code, _) = await _service.IssueAsync("0901234567");

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
}
