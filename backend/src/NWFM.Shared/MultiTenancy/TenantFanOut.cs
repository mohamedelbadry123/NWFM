using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace NWFM.Shared.MultiTenancy;

/// <summary>Runs background work once per serving tenant, each pass bound to that tenant only.</summary>
public interface ITenantFanOut
{
    /// <summary>
    /// Invokes <paramref name="work"/> for every serving tenant that passes the readiness gates, inside
    /// that tenant's ambient scope. Scopes the work creates therefore connect to that tenant's database.
    /// One tenant's failure is logged and never stops the others.
    /// </summary>
    Task ForEachServingTenantAsync(Func<TenantInfo, CancellationToken, Task> work, CancellationToken cancellationToken);
}

public sealed class TenantFanOut(
    ITenantStore store,
    ITenantContext context,
    IEnumerable<ITenantReadiness> readiness,
    IOptions<TenancyOptions> options,
    ILogger<TenantFanOut> logger) : ITenantFanOut
{
    private readonly ITenantReadiness[] _gates = readiness.ToArray();

    public Task ForEachServingTenantAsync(Func<TenantInfo, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var tenants = store.GetAll().Where(t => t.IsServing).ToArray();
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.Value.WorkerParallelism),
            CancellationToken = cancellationToken
        };
        return Parallel.ForEachAsync(tenants, parallel, async (tenant, ct) =>
        {
            foreach (var gate in _gates)
                if (await gate.CheckAsync(tenant, ct) is not null) return;

            using var tenantScope = context.BeginScope(tenant);
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["TenantId"] = tenant.Id,
                ["TenantKey"] = tenant.Key
            });
            try
            {
                await work(tenant, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Background work failed for tenant {TenantKey}; other tenants continue.", tenant.Key);
            }
        });
    }
}
