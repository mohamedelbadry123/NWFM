namespace NWFM.Shared.Options;

/// <summary>
/// Tokens for platform staff. A separate key and audience from <see cref="JwtSettings"/> so a
/// tenant token can never be replayed against the platform API, nor the reverse.
/// </summary>
public sealed class PlatformJwtSettings
{
    public const string SectionName = "PlatformJwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
