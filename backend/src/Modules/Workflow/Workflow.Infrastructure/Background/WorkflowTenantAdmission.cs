namespace Workflow.Infrastructure.Background;

using Microsoft.Extensions.DependencyInjection;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

/// <summary>
/// Background passes run inside one tenant's ambient scope (see <see cref="ITenantFanOut"/>) and
/// never take their tenant from a queued message's selector.
/// </summary>
internal static class WorkflowTenantAdmission
{
    /// <summary>Runs one poll pass for every serving tenant, each inside that tenant's ambient scope.</summary>
    public static async Task ForEachTenantAsync(IServiceScopeFactory scopes, Func<CancellationToken, Task> pass, CancellationToken ct)
    {
        ITenantFanOut fanOut;
        using (var scope = scopes.CreateScope())
            fanOut = scope.ServiceProvider.GetRequiredService<ITenantFanOut>();
        await fanOut.ForEachServingTenantAsync((_, token) => pass(token), ct);
    }

    public static async Task<bool> IsActiveAsync(IServiceProvider services, CancellationToken ct)
    {
        var tenant = services.GetService<ITenantContext>()?.Current;
        var availability = services.GetService<ITenantAvailability>();
        return tenant is { IsServing: true } && availability is not null
            && await availability.IsActiveAsync(ct);
    }

    public static async Task RequireMessageScopeAsync(IServiceProvider services, Guid organizationId, CancellationToken ct)
    {
        var tenant = services.GetService<ITenantContext>()?.Current;
        if (tenant is null || tenant.Id == Guid.Empty || tenant.Id != organizationId
            || !await IsActiveAsync(services, ct))
            throw new UnauthorizedAccessException("The queued operation is outside the active tenant.");
    }
}
