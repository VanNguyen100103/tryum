namespace Tryum.Infrastructure.Common;

/// <summary>
/// JWT configuration options, bind từ appsettings.json "Jwt" section.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Secret key (base64-encoded, ≥ 256 bits).
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Token issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Token audience.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime (phút). Default 60.
    /// </summary>
    public int ExpiresInMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token lifetime (ngày). Default 30.
    /// </summary>
    public int RefreshTokenExpiresInDays { get; set; } = 30;
}