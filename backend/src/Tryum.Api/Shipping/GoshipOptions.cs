namespace Tryum.Api.Shipping;

/// <summary>Cấu hình Goship API v2 (sandbox hoặc production).</summary>
public sealed class GoshipOptions
{
    public const string SectionName = "Goship";

    public string BaseUrl { get; set; } = "https://sandbox.goship.io/api/v2";
    public string AccessToken { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
