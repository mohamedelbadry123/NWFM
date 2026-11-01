namespace Tenancy.Domain.Entities;

/// <summary>
/// Per-tenant switches for the features that only some customers have. Owned by <see cref="Tenant"/>
/// and stored on its row. Integration secrets stay out of the catalog unless encrypted.
/// </summary>
public sealed class TenantSettings
{
    private TenantSettings()
    {
    }

    public bool SsoEnabled { get; private set; }

    /// <summary>Optional SP entity id for this tenant. Empty uses the platform <c>Sso:EntityId</c>.</summary>
    public string? SsoEntityId { get; private set; }

    /// <summary>Optional IdP entity id for this tenant. Empty uses the platform default.</summary>
    public string? SsoIdentityProviderEntityId { get; private set; }

    /// <summary>Optional IdP metadata URL for this tenant. Empty uses the platform default.</summary>
    public string? SsoMetadataUrl { get; private set; }

    /// <summary>Default locale for e-mails sent on the tenant's behalf (<c>en</c> or <c>ar</c>).</summary>
    public string DefaultLanguage { get; private set; } = Languages.English;

    public const int SsoUrlMaxLength = 400;

    /// <summary>
    /// The workspace appears on the public landing page's sign-in list. Off unless the tenant opts in:
    /// a public list of customers is information an attacker would otherwise have to guess.
    /// </summary>
    public bool ListedPublicly { get; private set; }

    public static TenantSettings Default() => new();

    public static TenantSettings Create(
        bool sso,
        string? defaultLanguage,
        string? ssoEntityId = null,
        string? ssoIdentityProviderEntityId = null,
        string? ssoMetadataUrl = null,
        bool listedPublicly = false) => new()
        {
            ListedPublicly = listedPublicly,
            SsoEnabled = sso,
            SsoEntityId = EmptyToNull(ssoEntityId),
            SsoIdentityProviderEntityId = EmptyToNull(ssoIdentityProviderEntityId),
            SsoMetadataUrl = EmptyToNull(ssoMetadataUrl),
            DefaultLanguage = defaultLanguage == Languages.Arabic ? Languages.Arabic : Languages.English
        };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static class Languages
    {
        public const string English = "en";
        public const string Arabic = "ar";
    }
}
