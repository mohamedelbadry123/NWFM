using NWFM.Shared.Abstractions;

namespace NWFM.Shared.MultiTenancy;

/// <summary>
/// <see cref="ICurrentTenant"/> over the ambient tenant. Empty when the flow is unbound, which the
/// tenant query filters treat as "no rows" and the write guard rejects.
/// </summary>
public sealed class AmbientCurrentTenant(ITenantContext context) : ICurrentTenant
{
    public Guid OrganizationId => context.Current?.Id ?? Guid.Empty;
}
