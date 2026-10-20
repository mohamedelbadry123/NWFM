namespace Workflow.Infrastructure.Background;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;

/// <summary>Background services use the server deployment identity, never a message's tenant selector.</summary>
internal static class WorkflowTenantAdmission
{
    public static async Task<bool> IsActiveAsync(IServiceProvider services, CancellationToken ct)
    {
        var tenant = services.GetService<IOptions<TenantIsolationOptions>>()?.Value;
        var availability = services.GetService<ITenantAvailability>();
        return tenant is { IsActive: true } && tenant.TenantId != Guid.Empty && availability is not null
            && await availability.IsActiveAsync(ct);
    }

    public static async Task RequireMessageScopeAsync(IServiceProvider services, Guid organizationId, CancellationToken ct)
    {
        var tenant = services.GetService<IOptions<TenantIsolationOptions>>()?.Value;
        if (tenant is not { IsActive: true } || tenant.TenantId == Guid.Empty || tenant.TenantId != organizationId
            || !await IsActiveAsync(services, ct))
            throw new UnauthorizedAccessException("The queued operation is outside the active tenant.");
    }
}
