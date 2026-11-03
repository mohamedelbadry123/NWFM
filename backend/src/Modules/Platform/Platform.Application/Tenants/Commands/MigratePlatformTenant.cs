using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Commands;

/// <summary>Applies pending migrations to one tenant now and returns the result.</summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record MigratePlatformTenantCommand(Guid TenantId) : IRequest<Result<TenantMigrationOutcome>>;

public sealed class MigratePlatformTenantCommandHandler(ITenantMigrationRunner runner)
    : IRequestHandler<MigratePlatformTenantCommand, Result<TenantMigrationOutcome>>
{
    public async Task<Result<TenantMigrationOutcome>> Handle(MigratePlatformTenantCommand request, CancellationToken cancellationToken)
    {
        var outcome = await runner.MigrateAsync(request.TenantId, cancellationToken);

        return outcome.Succeeded
            ? Result<TenantMigrationOutcome>.Success(outcome)
            : PlatformResults.Fail<TenantMigrationOutcome>(outcome.Error ?? "Migration failed.", ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 422);
    }
}

/// <summary>Queues a migration pass over every active tenant. Returns at once; results land in the audit log.</summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record MigrateAllPlatformTenantsCommand : IRequest<Result<bool>>;

public sealed class MigrateAllPlatformTenantsCommandHandler(IPlatformWorkQueue queue)
    : IRequestHandler<MigrateAllPlatformTenantsCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(MigrateAllPlatformTenantsCommand request, CancellationToken cancellationToken)
    {
        var queued = queue.Enqueue("migrate-all", (services, ct) =>
            services.GetRequiredService<ITenantMigrationRunner>().MigrateAllAsync(ct));

        return Task.FromResult(queued
            ? Result<bool>.Success(true)
            : PlatformResults.Fail<bool>("The platform work queue is full; try again shortly.", ApiErrorCodes.ServiceUnavailable, httpStatusCode: 503));
    }
}

/// <summary>Migrations the tenant database has not applied yet.</summary>
[PlatformAuthorize]
public sealed record GetPlatformTenantPendingMigrationsQuery(Guid TenantId) : IRequest<Result<IReadOnlyList<string>>>;

public sealed class GetPlatformTenantPendingMigrationsQueryHandler(ITenantStore tenantStore, ITenantDatabaseManager databaseManager)
    : IRequestHandler<GetPlatformTenantPendingMigrationsQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetPlatformTenantPendingMigrationsQuery request, CancellationToken cancellationToken)
    {
        if (tenantStore.FindById(request.TenantId) is not { } tenant)
        {
            return PlatformResults.Fail<IReadOnlyList<string>>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        return Result<IReadOnlyList<string>>.Success(await databaseManager.GetPendingMigrationsAsync(tenant, cancellationToken));
    }
}
