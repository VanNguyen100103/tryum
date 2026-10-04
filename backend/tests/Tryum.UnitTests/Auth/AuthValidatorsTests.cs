using Tryum.Application.Auth;

namespace Tryum.UnitTests.Auth;

public class AuthValidatorsTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Fact]
    public void Register_ValidRequest_ShouldPass()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "a@shop.vn", "Matkhau@123", "0901234567", "123456", "Tên cửa hàng")]
    [InlineData("Phở Thảo", "khong-phai-email", "Matkhau@123", "0901234567", "123456", "Email")]
    [InlineData("Phở Thảo", "a@shop.vn", "ngan", "0901234567", "123456", "Mật khẩu")]
    [InlineData("Phở Thảo", "a@shop.vn", "Matkhau@123", "12345", "123456", "Số điện thoại")]
    [InlineData("Phở Thảo", "a@shop.vn", "Matkhau@123", "0901234567", "12345", "Mã OTP")]
    [InlineData("Phở Thảo", "a@shop.vn", "Matkhau@123", "0123456789", "123456", "Số điện thoại")]
    public void Register_InvalidRequest_ShouldFailWithMessage(
        string shop, string email, string password, string phone, string otp, string expectedMessage)
    {
        var result = _validator.Validate(Valid() with
        {
            ShopName = shop,
            Email = email,
            Password = password,
            Phone = phone,
            OtpCode = otp,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(expectedMessage));
    }

    [Fact]
    public void Register_AcceptsCommonPhoneFormats()
    {
        foreach (var phone in new[] { "0901234567", "090 123 4567", "+84901234567", "84901234567", "0084901234567" })
        {
            var result = _validator.Validate(Valid() with { Phone = phone });

            Assert.True(
                result.IsValid,
                $"SĐT {phone} phải hợp lệ: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        }
    }

    [Fact]
    public void Register_PasswordWithoutDigit_ShouldFail()
    {
        var result = _validator.Validate(Valid() with { Password = "MatkhauCuaToi" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void SendOtp_InvalidPhone_ShouldFail()
    {
        var result = new SendOtpRequestValidator()
            .Validate(new SendOtpRequest("123", "captcha-id", "ABCD"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SendOtp_ValidPhone_ShouldPass()
    {
        Assert.True(new SendOtpRequestValidator()
            .Validate(new SendOtpRequest("090.123.4567", "captcha-id", "ABCD")).IsValid);
    }

    [Theory]
    [InlineData(null, "ABCD")]
    [InlineData("", "ABCD")]
    [InlineData("captcha-id", null)]
    [InlineData("captcha-id", "")]
    public void SendOtp_MissingCaptcha_ShouldFail(string? captchaId, string? captchaCode)
    {
        // Captcha là hàng rào chống bot đốt quota SMS => thiếu là reject.
        var result = new SendOtpRequestValidator()
            .Validate(new SendOtpRequest("0901234567", captchaId, captchaCode));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("owner@phothao.vn")]
    [InlineData("0901234567")]
    [InlineData("+84901234567")]
    [InlineData("090.123.4567")]
    public void Login_EmailOrPhone_ShouldPass(string identifier)
    {
        Assert.True(new LoginRequestValidator().Validate(new LoginRequest(identifier, "Matkhau@123")).IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789")]
    [InlineData("khong-phai-email")]
    public void Login_InvalidIdentifier_ShouldFail(string identifier)
    {
        Assert.False(new LoginRequestValidator().Validate(new LoginRequest(identifier, "Matkhau@123")).IsValid);
    }

    [Fact]
    public void Login_EmptyPassword_ShouldFail()
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest("a@shop.vn", string.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refresh_EmptyToken_ShouldFail()
    {
        Assert.False(new RefreshRequestValidator().Validate(new RefreshRequest(string.Empty)).IsValid);
    }

    private static RegisterRequest Valid() => new(
        ShopName: "Phở Thảo",
        OwnerName: "Nguyễn Văn A",
        Email: "a@shop.vn",
        Password: "Matkhau@123",
        Phone: "0901234567",
        OtpCode: "123456");
}
