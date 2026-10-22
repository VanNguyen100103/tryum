namespace Tryum.Infrastructure.Common;

public sealed class R2Options
{
    public const string SectionName = "R2";

    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(BucketName);
}
