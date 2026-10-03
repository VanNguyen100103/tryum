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
    [InlineData("", "a@shop.vn", "Matkhau@123", "Tên cửa hàng")]
    [InlineData("Phở Thảo", "khong-phai-email", "Matkhau@123", "Email")]
    [InlineData("Phở Thảo", "a@shop.vn", "ngan", "Mật khẩu")]
    public void Register_InvalidRequest_ShouldFailWithMessage(
        string shop, string email, string password, string expectedMessage)
    {
        var result = _validator.Validate(Valid() with
        {
            ShopName = shop,
            Email = email,
            Password = password,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(expectedMessage));
    }

    [Fact]
    public void Register_PasswordWithoutDigit_ShouldFail()
    {
        var result = _validator.Validate(Valid() with { Password = "MatkhauCuaToi" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Login_InvalidEmail_ShouldFail()
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest("abc", "Matkhau@123"));

        Assert.False(result.IsValid);
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
        Phone: "0901234567");
}