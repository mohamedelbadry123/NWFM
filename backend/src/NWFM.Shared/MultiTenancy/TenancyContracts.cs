namespace NWFM.Shared.MultiTenancy;

/// <summary>Turns a tenant into its database connection string. The only place a tenant secret is decrypted.</summary>
public interface ITenantConnectionStringResolver
{
    string Resolve(TenantInfo tenant);

    /// <summary>
    /// The connection string for the ambient tenant. Throws when there is none: a tenant database is
    /// never opened by a request, worker or startup step that has not been bound to a tenant.
    /// </summary>
    string ResolveForCurrentTenant();
}

/// <summary>Which credential a host uses against tenant databases.</summary>
public enum TenantConnectionRole
{
    /// <summary>The restricted request principal. Used by the tenant API.</summary>
    Runtime = 1,

    /// <summary>The database owner. Used only by the control plane to migrate, seed and grant.</summary>
    Administration = 2
}

/// <summary>Encrypts secrets stored in the catalog. Keys live in the catalog so every node can decrypt what another wrote.</summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

    string Unprotect(string purpose, string protectedText);
}

public static class SecretPurposes
{
    public const string TenantRuntimeConnection = "NWFM.Tenant.RuntimeConnection.v1";
    public const string TenantAdminConnection = "NWFM.Tenant.AdminConnection.v1";
    public const string DatabaseServerAdmin = "NWFM.DatabaseServer.Admin.v1";
    public const string SignupDatabasePassword = "NWFM.Signup.DatabasePassword.v1";
}

/// <summary>
/// An additional gate a tenant must pass before requests or workers reach its database (schema
/// version, database isolation verification). Implementations cache; the check runs per request.
/// </summary>
public interface ITenantReadiness
{
    ValueTask<TenantAccessException?> CheckAsync(TenantInfo tenant, CancellationToken cancellationToken);
}

/// <summary>
/// Binds the request to the tenant a validated token names, during authentication, so the session
/// re-validation that follows reads that tenant's database. Refuses an unknown tenant, one that is
/// not serving, one that is not ready, and a token whose tenant differs from one already bound.
/// </summary>
public interface ITenantSessionBinder
{
    ValueTask<TenantAccessException?> BindAsync(System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>Records a refused tenant resolution (unknown, mismatched, not serving) for security monitoring.</summary>
public interface ITenantRefusalSink
{
    void ResolutionRefused(Guid tenantId, string? tenantKey, string code);
}

/// <summary>Checks a support-session token against the catalog on every request, so revocation is immediate.</summary>
public interface ISupportSessionValidator
{
    Task<bool> IsActiveAsync(Guid sessionId, Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>The signed-in platform staff member, read from the platform token. Empty on tenant requests.</summary>
public interface IPlatformUser
{
    Guid? Id { get; }

    string? Email { get; }

    string? DisplayName { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }
}

/// <summary>Adds a platform audit row to the catalog. Saved with the caller's next <c>SaveChangesAsync</c>.</summary>
public interface IPlatformAuditWriter
{
    void Add(string action, Guid? tenantId, string? targetType = null, string? targetId = null, string? details = null);
}
