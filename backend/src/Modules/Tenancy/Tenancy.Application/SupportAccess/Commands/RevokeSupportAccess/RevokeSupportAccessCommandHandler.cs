using NWFM.Shared.Abstractions;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.Constants;

namespace Tenancy.Application.SupportAccess.Commands.RevokeSupportAccess;

public sealed class RevokeSupportAccessCommandHandler(
    ICatalogDbContext catalog,
    ITenantContext tenantContext,
    ICurrentUser user,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<RevokeSupportAccessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RevokeSupportAccessCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current ?? throw TenantAccessException.Required();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var grant = await catalog.SupportAccessGrants
            .FirstOrDefaultAsync(g => g.Id == request.GrantId && g.TenantId == tenant.Id, cancellationToken);

        if (grant is null)
        {
            return Result.Failure<bool>(TenancyErrors.SupportGrantNotFound);
        }

        grant.Revoke(user.Id ?? string.Empty, now);

        var openSessions = await catalog.SupportSessions
            .Where(s => s.GrantId == grant.Id && s.EndedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in openSessions)
        {
            session.End(now);
            audit.Add(PlatformAuditActions.SupportSessionEnded, tenant.Id, nameof(SupportSession), session.Id.ToString(),
                "Ended because the workspace revoked the grant.");
        }

        audit.Add(PlatformAuditActions.SupportRevoked, tenant.Id, nameof(SupportAccessGrant), grant.Id.ToString());
        await catalog.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
