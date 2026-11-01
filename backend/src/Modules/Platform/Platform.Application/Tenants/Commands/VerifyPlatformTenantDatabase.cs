using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Commands;

/// <summary>
/// Re-runs the isolation checks against a workspace's runtime credential (no admin rights, no other
/// database, no impersonation, matching boundary) and records when it last passed.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record VerifyPlatformTenantDatabaseCommand(Guid TenantId) : IRequest<Result<bool>>;

public sealed class VerifyPlatformTenantDatabaseCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantDatabaseVerification verification,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<VerifyPlatformTenantDatabaseCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(VerifyPlatformTenantDatabaseCommand request, CancellationToken cancellationToken)
    {
        if (tenantStore.FindById(request.TenantId) is not { } tenant)
        {
            return PlatformResults.Fail<bool>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        try
        {
            if (!await verification.VerifyRuntimeAsync(tenant, cancellationToken))
            {
                return PlatformResults.Fail<bool>(
                    "This workspace's database is defined in configuration; the tenant API verifies it on first use.",
                    ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 422);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            audit.Add(PlatformAuditActions.TenantDatabaseVerificationFailed, tenant.Id, nameof(TenantDatabase), tenant.Key, error.Message);
            await catalog.SaveChangesAsync(cancellationToken);
            return PlatformResults.Fail<bool>(error.Message, ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 422);
        }

        var database = await catalog.TenantDatabases.FirstAsync(d => d.TenantId == tenant.Id, cancellationToken);
        database.RecordVerification(timeProvider.GetUtcNow().UtcDateTime);
        audit.Add(PlatformAuditActions.TenantDatabaseVerified, tenant.Id, nameof(TenantDatabase), tenant.Key);
        await catalog.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
