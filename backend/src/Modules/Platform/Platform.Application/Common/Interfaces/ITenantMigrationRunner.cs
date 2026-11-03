namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Brings tenant databases to the current schema outside startup, one tenant at a time, recording
/// each result in the catalog. A tenant that fails is put into maintenance; the rest carry on.
/// </summary>
public interface ITenantMigrationRunner
{
    /// <returns>The last applied migration, or null when the tenant could not be migrated.</returns>
    Task<TenantMigrationOutcome> MigrateAsync(Guid tenantId, CancellationToken cancellationToken);

    Task MigrateAllAsync(CancellationToken cancellationToken);
}

public sealed record TenantMigrationOutcome(bool Succeeded, string? SchemaVersion, string? Error);
