using Tryum.Infrastructure.Common;

namespace Tryum.UnitTests.Sales;

/// <summary>
/// Kiểm tra phần bảo mật webhook SePay và tách mã hóa đơn – hai chỗ dễ sai nhất
/// khi tích hợp thanh toán chuyển khoản (chữ ký lệch → mất tiền oan, tách mã sai → treo đơn).
/// </summary>
public class SePayWebhookTests
{
    private const string Secret = "dev-secret";
    private const string Timestamp = "1700000000";
    private const string Body =
        "{\"id\":92704,\"transferAmount\":500000,\"content\":\"HD000123 chuyen tien\",\"transferType\":\"in\"}";

    // Vector tính sẵn bằng: HMAC_SHA256(secret, "{timestamp}.{body}") → hex.
    private const string ExpectedSignature =
        "sha256=56a561b74523ee4834b21bf8f28526a5592e8eac0e2586f245819510c944c38a";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(Timestamp)).AddSeconds(5);

    [Fact]
    public void ComputeSignature_MatchesKnownVector()
    {
        Assert.Equal(ExpectedSignature, SePayWebhookVerifier.ComputeSignature(Secret, Timestamp, Body));
    }

    [Fact]
    public void VerifySignature_Valid_ReturnsTrue()
    {
        Assert.True(SePayWebhookVerifier.VerifySignature(
            Secret, Timestamp, Body, ExpectedSignature, 300, Now));
    }

    [Fact]
    public void VerifySignature_AcceptsUppercaseHex()
    {
        var uppercase = "sha256=" + ExpectedSignature["sha256=".Length..].ToUpperInvariant();

        Assert.True(SePayWebhookVerifier.VerifySignature(Secret, Timestamp, Body, uppercase, 300, Now));
    }

    [Fact]
    public void VerifySignature_TamperedBody_ReturnsFalse()
    {
        Assert.False(SePayWebhookVerifier.VerifySignature(
            Secret, Timestamp, Body.Replace("500000", "900000"), ExpectedSignature, 300, Now));
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnsFalse()
    {
        Assert.False(SePayWebhookVerifier.VerifySignature(
            "another-secret", Timestamp, Body, ExpectedSignature, 300, Now));
    }

    [Fact]
    public void VerifySignature_ExpiredTimestamp_ReturnsFalse()
    {
        var tooLate = Now.AddSeconds(301);

        Assert.False(SePayWebhookVerifier.VerifySignature(
            Secret, Timestamp, Body, ExpectedSignature, 300, tooLate));
    }

    [Fact]
    public void VerifySignature_MissingHeaders_ReturnsFalse()
    {
        Assert.False(SePayWebhookVerifier.VerifySignature(Secret, null, Body, ExpectedSignature, 300, Now));
        Assert.False(SePayWebhookVerifier.VerifySignature(Secret, Timestamp, Body, null, 300, Now));
        Assert.False(SePayWebhookVerifier.VerifySignature(null, Timestamp, Body, ExpectedSignature, 300, Now));
    }

    [Theory]
    [InlineData("Apikey my-secret-key", "my-secret-key", true)]
    [InlineData("apikey my-secret-key", "my-secret-key", true)]
    [InlineData("Bearer my-secret-key", "my-secret-key", false)]
    [InlineData("Apikey wrong", "my-secret-key", false)]
    [InlineData("", "my-secret-key", false)]
    public void VerifyApiKey_BehavesAsDocumented(string header, string expectedKey, bool expected)
    {
        Assert.Equal(expected, SePayWebhookVerifier.VerifyApiKey(expectedKey, header));
    }

    [Theory]
    [InlineData("HD000123 chuyen tien", "HD000123")]
    [InlineData("MBVCB.123456.HD000045.CT tu NGUYEN VAN A", "HD000045")]
    [InlineData("hd999999", "HD999999")]
    [InlineData("SEVN63DC8E5C thanh toan HD2024 12", "HD2024")]
    [InlineData("khong co ma nao", null)]
    [InlineData("HD12", null)]
    [InlineData(null, null)]
    public void ExtractOrderCode_WorksWithBankNarratives(string? content, string? expected)
    {
        Assert.Equal(expected, SePayTransactionParser.ExtractOrderCode(content));
    }

    [Theory]
    [InlineData("POS123456789", "POS123456789")]
    [InlineData("MBVCB.POS696835010.CT tu KHACH", "POS696835010")]
    [InlineData("pos999999", "POS999999")]
    [InlineData("POS123", null)]
    [InlineData("HD000123", null)]
    [InlineData(null, null)]
    public void ExtractPosReference_WorksWithBankNarratives(string? content, string? expected)
    {
        Assert.Equal(expected, SePayTransactionParser.ExtractPosReference(content));
    }

    [Fact]
    public void SePayOptions_ConfiguredFlags()
    {
        var empty = new SePayOptions();
        Assert.False(empty.IsBankConfigured);
        Assert.False(empty.IsWebhookSecured);

        var configured = new SePayOptions
        {
            BankBin = "970436",
            AccountNumber = "0071000123456",
            WebhookSecret = "s3cret",
        };
        Assert.True(configured.IsBankConfigured);
        Assert.True(configured.IsWebhookSecured);
    }
}
