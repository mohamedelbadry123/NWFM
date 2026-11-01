using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Infrastructure.Services;
using Tenancy.Domain.Constants;
using Tenancy.Infrastructure.Persistence;
using Tenancy.Infrastructure.Security;

namespace Platform.Infrastructure;

public static class PlatformInitialiserExtensions
{
    public static async Task InitialisePlatformAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<PlatformInitialiser>();
        await initialiser.InitialiseAsync(app.Lifetime.ApplicationStopping);
    }
}

/// <summary>
/// Startup in two halves: the catalog (migrate, seed plans, register the bootstrap tenant and the
/// first platform administrator), then each tenant database under its own tenant scope. One tenant
/// failing to migrate is put into maintenance and the rest still start.
/// </summary>
public sealed class PlatformInitialiser(
    CatalogDbContext catalog,
    ITenantContext tenantContext,
    ITenantStore tenantStore,
    ITenantDatabaseManager databaseManager,
    IOptions<DatabaseStartupOptions> startupOptions,
    IOptions<TenancyOptions> tenancyOptions,
    IOptions<PlatformOptions> platformOptions,
    IPasswordHasher<PlatformUser> passwordHasher,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<PlatformInitialiser> logger)
{
    private const string MigrationFailedReason = TenantStatusReasons.MigrationFailed;

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        using var platformScope = tenantContext.BeginPlatformScope();

        await InitialiseCatalogAsync(cancellationToken);
        // Before any tenant API can start: it reads this ring and may not create keys itself.
        KeyRingRotation.EnsureCurrentKey(dataProtection);
        tenantStore.Invalidate();

        foreach (var tenant in tenantStore.GetAll().Where(t => t.Status is TenantStatus.Active or TenantStatus.Maintenance))
        {
            await InitialiseTenantAsync(tenant, cancellationToken);
        }

        tenantStore.Invalidate();
    }

    private async Task InitialiseCatalogAsync(CancellationToken cancellationToken)
    {
        if (!catalog.Database.GetMigrations().Any())
        {
            throw new InvalidOperationException(
                "The catalog has no migrations. Add one: dotnet ef migrations add InitialCatalog --context CatalogDbContext " +
                "--output-dir Persistence/Migrations --project src/backend/src/Modules/Platform/Platform.Infrastructure " +
                "--startup-project src/backend/src/NWFM.Platform.Api");
        }

        if (startupOptions.Value.ApplyMigrations)
        {
            logger.LogInformation("Applying catalog migrations.");
            await catalog.Database.MigrateAsync(cancellationToken);
        }

        if (!startupOptions.Value.SeedData)
        {
            return;
        }

        await SeedPlansAsync(cancellationToken);
        await RegisterConfigurationTenantsAsync(cancellationToken);
        await BootstrapPlatformAdminAsync(cancellationToken);
    }

    private async Task SeedPlansAsync(CancellationToken cancellationToken)
    {
        if (await catalog.Plans.AnyAsync(cancellationToken))
        {
            return;
        }

        catalog.Plans.AddRange(PlanSeedData.Create());
        await catalog.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the default plans.");
    }

    /// <summary>
    /// Registers every configuration tenant (the development workspace, e2e tenants, adopted
    /// single-tenant deployments) that the catalog does not know yet, under the id its database is
    /// already bound to. Its connection string stays in configuration and is referenced by name; each
    /// host resolves that name with its own credential. Other tenants come from signup or the console.
    /// </summary>
    private async Task RegisterConfigurationTenantsAsync(CancellationToken cancellationToken)
    {
        foreach (var configured in tenancyOptions.Value.ConfigurationTenants)
        {
            if (configured.Id == Guid.Empty || string.IsNullOrWhiteSpace(configured.Key))
            {
                throw new InvalidOperationException("Every Tenancy:ConfigurationTenants entry needs an Id and a Key.");
            }

            var key = TenantKey.Normalize(configured.Key);
            var existing = await catalog.Tenants.FirstOrDefaultAsync(t => t.Id == configured.Id || t.Key == key, cancellationToken);
            if (existing is not null)
            {
                if (existing.Id != configured.Id || existing.Key != key)
                {
                    throw new InvalidOperationException(
                        $"Configuration tenant '{key}' ({configured.Id}) conflicts with catalog tenant '{existing.Key}' ({existing.Id}).");
                }

                continue;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var plan = await catalog.Plans.FirstOrDefaultAsync(p => p.Code == configured.PlanCode, cancellationToken)
                ?? await catalog.Plans.FirstAsync(p => p.Code == PlanSeedData.Enterprise, cancellationToken);

            var tenant = Tenant.Create(
                key,
                string.IsNullOrWhiteSpace(configured.Name) ? key : configured.Name,
                configured.ContactName,
                string.IsNullOrWhiteSpace(configured.ContactEmail) ? "admin@localhost" : configured.ContactEmail,
                contactPhone: null,
                configured.SeedProfile,
                now,
                configured.Id);

            tenant.UseStoragePath(configured.StoragePath);
            tenant.ChangeSettings(TenantSettings.Create(configured.SsoEnabled, TenantSettings.Languages.English));

            foreach (var host in configured.Hosts)
            {
                tenant.AddHost(host);
            }

            tenant.AttachDatabase(TenantDatabase.CreateFromConfiguration(tenant.Id, configured.ConnectionStringName, configured.RuntimePrincipal));
            tenant.StartSubscription(Subscription.Start(tenant.Id, plan, now));
            tenant.Activate(now);

            catalog.Tenants.Add(tenant);
            catalog.PlatformAuditEntries.Add(PlatformAuditEntry.Create(
                PlatformAuditActions.TenantCreated, null, "system", tenant.Id, nameof(Tenant), tenant.Key,
                "Registered a configuration tenant.", null, now));

            await catalog.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Registered configuration tenant {TenantKey} on connection {ConnectionName}.", tenant.Key, configured.ConnectionStringName);
        }
    }

    private async Task BootstrapPlatformAdminAsync(CancellationToken cancellationToken)
    {
        var bootstrap = platformOptions.Value.BootstrapAdmin;

        if (string.IsNullOrWhiteSpace(bootstrap.Email)
            || string.IsNullOrWhiteSpace(bootstrap.Password)
            || await catalog.PlatformUsers.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var placeholder = PlatformUser.Create(bootstrap.Email, bootstrap.DisplayName, PlatformRoles.Admin, "pending", now);
        placeholder.ChangePasswordHash(passwordHasher.HashPassword(placeholder, bootstrap.Password));

        catalog.PlatformUsers.Add(placeholder);
        await catalog.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Created the bootstrap platform administrator {Email}. Change its password and remove Platform:BootstrapAdmin:Password.", bootstrap.Email);
    }

    private async Task InitialiseTenantAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        var isConfigured = tenancyOptions.Value.ConfigurationTenants.Any(c => c.Id == tenant.Id);

        if (!isConfigured && !tenancyOptions.Value.MigrateTenantsOnStartup)
        {
            return;
        }

        var tenantRow = await catalog.Tenants.Include(t => t.Database).FirstAsync(t => t.Id == tenant.Id, cancellationToken);

        try
        {
            var version = await databaseManager.MigrateAsync(tenant, cancellationToken);

            if (startupOptions.Value.SeedData)
            {
                await databaseManager.SeedAsync(tenant, tenantRow.SeedProfile, cancellationToken);
            }

            tenantRow.Database?.RecordMigration(version, timeProvider.GetUtcNow().UtcDateTime);

            if (tenantRow.Status == TenantStatus.Maintenance && tenantRow.StatusReason == MigrationFailedReason)
            {
                tenantRow.Activate(timeProvider.GetUtcNow().UtcDateTime);
            }

            await catalog.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Initialising tenant {TenantKey} failed; it is placed in maintenance.", tenant.Key);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            tenantRow.Database?.RecordMigrationFailure(ex.Message, now);
            tenantRow.EnterMaintenance(MigrationFailedReason, now);
            catalog.PlatformAuditEntries.Add(PlatformAuditEntry.Create(
                PlatformAuditActions.TenantMigrationFailed, null, "system", tenant.Id, nameof(Tenant), tenant.Key, ex.Message, null, now));
            await catalog.SaveChangesAsync(cancellationToken);
        }
    }
}
