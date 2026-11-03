using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// Runs every registered <see cref="ITenantDatabaseModule"/> against one tenant database, in
/// <see cref="ITenantDatabaseModule.Order"/>, inside that tenant's ambient scope and a fresh DI scope
/// so each module's DbContext connects to that tenant. Only the control plane calls it.
/// </summary>
public sealed class TenantDatabaseManager(
    IEnumerable<ITenantDatabaseModule> modules,
    ITenantContext tenantContext,
    IServiceScopeFactory scopes,
    ILogger<TenantDatabaseManager> logger)
{
    private readonly ITenantDatabaseModule[] _modules = modules.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ITenantDatabaseModule> Modules => _modules;

    /// <summary>Migrates every module and returns the composite schema version to record in the catalog.</summary>
    public async Task<string?> MigrateAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        await RunAsync(tenant, async (module, services) =>
        {
            logger.LogInformation("Migrating {Module} for tenant {TenantKey}.", module.Name, tenant.Key);
            await module.MigrateAsync(services, cancellationToken);
        });
        return TenantSchema.Compose(_modules);
    }

    public Task SeedAsync(TenantInfo tenant, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        RunAsync(tenant, async (module, services) =>
        {
            logger.LogInformation("Seeding {Module} ({Profile}) for tenant {TenantKey}.", module.Name, profile, tenant.Key);
            await module.SeedAsync(services, profile, cancellationToken);
        });

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetPendingMigrationsAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        var pending = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        await RunAsync(tenant, async (module, services) =>
        {
            var list = await module.GetPendingMigrationsAsync(services, cancellationToken);
            if (list.Count > 0) pending[module.Name] = list;
        });
        return pending;
    }

    private async Task RunAsync(TenantInfo tenant, Func<ITenantDatabaseModule, IServiceProvider, Task> step)
    {
        using var tenantScope = tenantContext.BeginScope(tenant);
        foreach (var module in _modules)
        {
            // A fresh scope per module: one module's change tracker never carries into the next.
            await using var scope = scopes.CreateAsyncScope();
            await step(module, scope.ServiceProvider);
        }
    }
}
