
namespace Platform.Application.Common.Interfaces;

/// <summary>Schema and seed operations against one tenant database, run outside any HTTP tenant scope.</summary>
public interface ITenantDatabaseManager
{
    /// <summary>Applies pending migrations and programmable objects; returns the last migration id.</summary>
    Task<string?> MigrateAsync(TenantInfo tenant, CancellationToken cancellationToken);

    Task SeedAsync(TenantInfo tenant, TenantSeedProfile profile, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(TenantInfo tenant, CancellationToken cancellationToken);
}

/// <summary>Creates the database and least-privilege login for a managed tenant.</summary>
public interface IManagedDatabaseProvisioner
{
    /// <summary>
    /// Idempotent: an existing database or login with the expected name is reused and the password
    /// rotated. Returns the tenant connection string (plaintext; the caller encrypts it).
    /// </summary>
    Task<ManagedDatabaseResult> CreateAsync(DatabaseServer server, string tenantKey, CancellationToken cancellationToken);
}

public sealed record ManagedDatabaseResult(string DatabaseName, string LoginName, string ConnectionString);

/// <summary>
/// Signs in as a tenant's runtime principal and proves it is confined to that tenant's database
/// (see <c>TenantDatabaseVerifier</c>). Run before a tenant is activated and on demand from the
/// console. Configuration tenants are verified by the tenant API, which holds their runtime credential.
/// </summary>
public interface ITenantDatabaseVerification
{
    /// <returns>False for a configuration tenant (not checkable here); throws with the failed check otherwise.</returns>
    Task<bool> VerifyRuntimeAsync(TenantInfo tenant, CancellationToken cancellationToken);
}
