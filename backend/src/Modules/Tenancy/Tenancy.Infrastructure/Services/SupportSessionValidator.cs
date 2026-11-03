using Microsoft.EntityFrameworkCore;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Services;

/// <summary>
/// One indexed catalog read per support-session request. Uncached on purpose: the tenant revoking
/// its grant has to cut the session off on the next call, on every node.
/// </summary>
internal sealed class SupportSessionValidator(CatalogDbContext catalog, TimeProvider timeProvider) : ISupportSessionValidator
{
    public async Task<bool> IsActiveAsync(Guid sessionId, Guid tenantId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return await catalog.SupportSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.TenantId == tenantId && s.EndedAtUtc == null && s.ExpiresAtUtc > now)
            .Join(catalog.SupportAccessGrants, s => s.GrantId, g => g.Id, (_, g) => g)
            .AnyAsync(g => g.RevokedAtUtc == null && g.ExpiresAtUtc > now, cancellationToken);
    }
}
