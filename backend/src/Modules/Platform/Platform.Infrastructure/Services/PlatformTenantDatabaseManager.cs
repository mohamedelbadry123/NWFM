using Platform.Application.Common.Interfaces;
using SharedManager = NWFM.Shared.MultiTenancy.TenantDatabaseManager;

namespace Platform.Infrastructure.Services;

/// <summary>
/// The control plane's view of <see cref="SharedManager"/>: every module that owns tenant objects,
/// in order, inside the tenant's scope and a fresh DI scope per module. The only way to reach a tenant
/// database other than a request's own, and never called from inside a tenant request.
/// </summary>
internal sealed class PlatformTenantDatabaseManager(SharedManager manager) : ITenantDatabaseManager
{
    public Task<string?> MigrateAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        manager.MigrateAsync(tenant, cancellationToken);

    public Task SeedAsync(TenantInfo tenant, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        manager.SeedAsync(tenant, profile, cancellationToken);

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        [.. (await manager.GetPendingMigrationsAsync(tenant, cancellationToken))
            .SelectMany(module => module.Value.Select(migration => $"{module.Key}: {migration}"))];
}
