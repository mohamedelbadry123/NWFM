using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.Constants;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Commands.ExchangeSupportHandoff;

public sealed class ExchangeSupportHandoffCommandHandler(
    ICatalogDbContext catalog,
    ITenantContext tenantContext,
    ISupportSessionTokenIssuer tokenIssuer,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<ExchangeSupportHandoffCommand, Result<SupportSessionTokenDto>>
{
    public async Task<Result<SupportSessionTokenDto>> Handle(ExchangeSupportHandoffCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current ?? throw TenantAccessException.Required();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = SupportSession.HashHandoffCode(request.Code);

        var session = await catalog.SupportSessions
            .FirstOrDefaultAsync(s => s.HandoffCodeHash == hash && s.TenantId == tenant.Id, cancellationToken);

        if (session is null)
        {
            return Result.Failure<SupportSessionTokenDto>(TenancyErrors.HandoffInvalid);
        }

        // Throws DomainException for a spent, late or ended session; the exception handler maps it.
        session.RedeemHandoff(request.Code, now);

        var token = await tokenIssuer.IssueAsync(
            new SupportSessionTicket(
                session.Id,
                session.TenantId,
                tenant.Key,
                session.PlatformUserId,
                session.PlatformUserName,
                session.Scope.ToClaimValue(),
                session.ImpersonatedUserId,
                session.ExpiresAtUtc),
            cancellationToken);

        if (token.IsFailure)
        {
            return token;
        }

        audit.Add(PlatformAuditActions.SupportSessionRedeemed, tenant.Id, nameof(SupportSession), session.Id.ToString());
        await catalog.SaveChangesAsync(cancellationToken);

        return token;
    }
}
