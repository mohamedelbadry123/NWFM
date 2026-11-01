using System.Security.Claims;
using NWFM.Shared.MultiTenancy;

namespace Tenancy.Api.Middleware;

/// <summary>
/// Binds the request to the tenant named by a cryptographically valid token, inside the bearer
/// handler. Exactly one <c>tid</c> claim is accepted; the tenant must exist, serve and pass the
/// readiness gates. A tenant already bound to the request (never by a client selector) must match.
/// </summary>
public sealed class TenantSessionBinder(
    ITenantContext tenantContext,
    ITenantStore tenantStore,
    IEnumerable<ITenantReadiness> readiness) : ITenantSessionBinder
{
    private readonly ITenantReadiness[] _gates = readiness.ToArray();

    public async ValueTask<TenantAccessException?> BindAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var claims = principal.FindAll(TenantClaimTypes.TenantId).Take(2).ToArray();
        if (claims.Length != 1 || !Guid.TryParse(claims[0].Value, out var tenantId) || tenantId == Guid.Empty)
            return new TenantAccessException("The sign-in does not name a workspace. Sign in again.",
                TenancyErrorCodes.TenantRequired, 401);

        if (tenantStore.FindById(tenantId) is not { } tenant)
            return TenantAccessException.NotFound();
        if (tenantContext.Current is { } bound && bound.Id != tenant.Id)
            return TenantAccessException.Mismatch();
        if (TenantAccessException.ForStatus(tenant) is { } status)
            return status;
        foreach (var gate in _gates)
            if (await gate.CheckAsync(tenant, cancellationToken) is { } refusal)
                return refusal;

        tenantContext.SetTenant(tenant);
        return null;
    }
}
