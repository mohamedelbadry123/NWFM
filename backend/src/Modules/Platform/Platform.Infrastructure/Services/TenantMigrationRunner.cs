using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Tenancy.Infrastructure.Persistence;

namespace Platform.Infrastructure.Services;

internal sealed class TenantMigrationRunner(
    CatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantDatabaseManager databaseManager,
    TimeProvider timeProvider,
    ILogger<TenantMigrationRunner> logger)
    : ITenantMigrationRunner
{
    public async Task<TenantMigrationOutcome> MigrateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await catalog.Tenants.Include(t => t.Database).FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var info = tenantStore.FindById(tenantId);

        if (tenant?.Database is null || info is null)
        {
            return new TenantMigrationOutcome(false, null, "The workspace has no database to migrate.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var version = await databaseManager.MigrateAsync(info, cancellationToken);
            tenant.Database.RecordMigration(version, timeProvider.GetUtcNow().UtcDateTime);

            if (tenant.Status == TenantStatus.Maintenance && tenant.StatusReason == TenantStatusReasons.MigrationFailed)
            {
                tenant.Activate(now);
            }

            catalog.PlatformAuditEntries.Add(PlatformAuditEntry.Create(
                PlatformAuditActions.TenantMigrated, null, "system", tenant.Id, nameof(Tenant), tenant.Key, version, null, now));
            await catalog.SaveChangesAsync(cancellationToken);
            tenantStore.Invalidate();

            return new TenantMigrationOutcome(true, version, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Migrating tenant {TenantKey} failed; it is placed in maintenance.", tenant.Key);

            tenant.Database.RecordMigrationFailure(ex.Message, now);
            if (tenant.Status == TenantStatus.Active)
            {
                tenant.EnterMaintenance(TenantStatusReasons.MigrationFailed, now);
            }

            catalog.PlatformAuditEntries.Add(PlatformAuditEntry.Create(
                PlatformAuditActions.TenantMigrationFailed, null, "system", tenant.Id, nameof(Tenant), tenant.Key, ex.Message, null, now));
            await catalog.SaveChangesAsync(cancellationToken);
            tenantStore.Invalidate();

            return new TenantMigrationOutcome(false, null, ex.Message);
        }
    }

    public async Task MigrateAllAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await catalog.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Maintenance)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            await MigrateAsync(tenantId, cancellationToken);
        }
    }
}
