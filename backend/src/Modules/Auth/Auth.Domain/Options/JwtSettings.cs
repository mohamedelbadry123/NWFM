namespace Auth.Domain.Options;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Issuer { get; set; } = "NWFM";
    public string Audience { get; set; } = "NWFM.Admin";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 600;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
