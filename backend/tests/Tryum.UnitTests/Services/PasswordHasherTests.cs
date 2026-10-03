using Tryum.Infrastructure.Services;

namespace Tryum.UnitTests.Services;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ShouldNotReturnPlainText()
    {
        var hash = _hasher.Hash("Matkhau@123");

        Assert.NotEqual("Matkhau@123", hash);
        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void Hash_ShouldProduceDifferentHash_ForSamePassword()
    {
        // Salt ngẫu nhiên => 2 lần hash cho ra 2 chuỗi khác nhau.
        Assert.NotEqual(_hasher.Hash("Matkhau@123"), _hasher.Hash("Matkhau@123"));
    }

    [Theory]
    [InlineData("Matkhau@123", true)]
    [InlineData("matkhau@123", false)]
    [InlineData("", false)]
    public void Verify_ShouldReturnExpectedResult(string password, bool expected)
    {
        var hash = _hasher.Hash("Matkhau@123");

        Assert.Equal(expected, _hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenHashIsCorrupted()
    {
        Assert.False(_hasher.Verify("Matkhau@123", "khong-phai-bcrypt-hash"));
    }

    [Fact]
    public void Hash_ShouldThrow_WhenPasswordEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(string.Empty));
    }
}