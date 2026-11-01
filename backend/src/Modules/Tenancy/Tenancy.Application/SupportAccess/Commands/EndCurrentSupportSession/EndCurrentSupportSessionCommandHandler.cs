using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.Constants;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Commands.EndCurrentSupportSession;

public sealed class EndCurrentSupportSessionCommandHandler(
    ICatalogDbContext catalog,
    ITenantContext tenantContext,
    ICurrentUser user,
    IPlatformAuditWriter audit,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
    : IRequestHandler<EndCurrentSupportSessionCommand, Result<SupportSessionEndedDto>>
{
    private const string TenantsPath = "/tenants/";

    public async Task<Result<SupportSessionEndedDto>> Handle(EndCurrentSupportSessionCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current ?? throw TenantAccessException.Required();

        if (user.SupportSessionId is not { } sessionId)
        {
            return Result.Failure<SupportSessionEndedDto>(TenancyErrors.NotASupportSession);
        }

        var session = await catalog.SupportSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenant.Id, cancellationToken);

        if (session is null)
        {
            return Result.Failure<SupportSessionEndedDto>(TenancyErrors.SupportSessionNotFound);
        }

        session.End(timeProvider.GetUtcNow().UtcDateTime);
        audit.Add(PlatformAuditActions.SupportSessionEnded, tenant.Id, nameof(SupportSession), session.Id.ToString(),
            "Ended from inside the workspace.");
        await catalog.SaveChangesAsync(cancellationToken);

        return Result.Success(new SupportSessionEndedDto
        {
            SessionId = session.Id,
            ReturnUrl = platformOptions.Value.ConsoleUrl.TrimEnd('/') + TenantsPath + tenant.Id
        });
    }
}
