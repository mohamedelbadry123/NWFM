using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.Constants;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Commands.GrantSupportAccess;

public sealed class GrantSupportAccessCommandHandler(
    ICatalogDbContext catalog,
    ITenantContext tenantContext,
    ICurrentUser user,
    IPlatformAuditWriter audit,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
    : IRequestHandler<GrantSupportAccessCommand, Result<SupportAccessGrantDto>>
{
    public async Task<Result<SupportAccessGrantDto>> Handle(GrantSupportAccessCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current ?? throw TenantAccessException.Required();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxHours = platformOptions.Value.MaxSupportGrantHours;
        var scope = request.Scope.ToSupportAccessScope();

        if (request.Hours > maxHours)
        {
            return Result.Failure<SupportAccessGrantDto>(TenancyErrors.GrantTooLong(maxHours));
        }

        var grant = SupportAccessGrant.Create(
            tenant.Id,
            scope,
            request.Reason,
            request.Hours,
            maxHours,
            user.Id ?? string.Empty,
            user.UserName ?? user.Email ?? user.Id ?? "tenant-admin",
            now);

        catalog.SupportAccessGrants.Add(grant);
        audit.Add(PlatformAuditActions.SupportGranted, tenant.Id, nameof(SupportAccessGrant), grant.Id.ToString(),
            $"{scope} for {request.Hours}h: {request.Reason}");
        await catalog.SaveChangesAsync(cancellationToken);

        return Result.Success(grant.ToDto(now));
    }
}
