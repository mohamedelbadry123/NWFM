namespace Auth.Domain.Options;

public sealed class SsoSettings
{
    public const string SectionName = "Sso";

    public bool Enabled { get; set; }
    public bool AllowLocalLoginForAdministrators { get; set; } = true;
    public string EntityId { get; set; } = string.Empty;
    public string ModulePath { get; set; } = "/api/v1/auth/sso/saml";
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string IdentityProviderEntityId { get; set; } = string.Empty;
    public string IdentityProviderMetadataUrl { get; set; } = string.Empty;
    public string LogoutUrl { get; set; } = string.Empty;
    public string CertPfxFile { get; set; } = string.Empty;
    public string CertPfxPassword { get; set; } = string.Empty;
    public string ClientAppBaseUrl { get; set; } = "http://localhost:4200";
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 60;
}
