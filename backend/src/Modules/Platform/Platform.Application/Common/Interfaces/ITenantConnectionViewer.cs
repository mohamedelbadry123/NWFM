
namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Shows staff where a tenant's database is, for support. Decrypts through the tenant connection
/// resolver — still the only place a tenant secret is unprotected — and masks the password unless
/// the caller explicitly asks to reveal it.
/// </summary>
public interface ITenantConnectionViewer
{
    /// <summary>Null when the tenant has no database or its connection string cannot be read.</summary>
    TenantConnectionDto? Describe(TenantInfo tenant, bool reveal);
}

public sealed record TenantConnectionDto
{
    public string? Server { get; init; }

    public string? Database { get; init; }

    public string? UserId { get; init; }

    public bool IntegratedSecurity { get; init; }

    /// <summary>The connection string with the password replaced.</summary>
    public string Masked { get; init; } = default!;

    /// <summary>The full connection string. Only set on an audited reveal.</summary>
    public string? Full { get; init; }
}
