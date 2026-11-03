using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Tenants.Common;

internal static class PlatformTenantQueries
{
    public static Task<Tenant?> LoadAsync(ICatalogDbContext catalog, Guid tenantId, CancellationToken cancellationToken) =>
        catalog.Tenants
            .Include(t => t.Hosts)
            .Include(t => t.Database)
            .Include(t => t.Subscription!).ThenInclude(s => s.Plan)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public static PlatformTenantDetailDto ToDetailDto(this Tenant tenant) => new()
    {
        Id = tenant.Id,
        Key = tenant.Key,
        Name = tenant.Name,
        Status = tenant.Status.ToString(),
        StatusReason = tenant.StatusReason,
        ContactName = tenant.ContactName,
        ContactEmail = tenant.ContactEmail,
        ContactPhone = tenant.ContactPhone,
        SeedProfile = tenant.SeedProfile.ToString(),
        StoragePath = tenant.StoragePath,
        CreatedAtUtc = tenant.CreatedAtUtc,
        ActivatedAtUtc = tenant.ActivatedAtUtc,
        Hosts = [.. tenant.Hosts.Select(h => h.Host).Order(StringComparer.Ordinal)],
        Settings = new PlatformTenantSettingsDto
        {
            SsoEnabled = tenant.Settings.SsoEnabled,
            ListedPublicly = tenant.Settings.ListedPublicly,
            SsoEntityId = tenant.Settings.SsoEntityId,
            SsoIdentityProviderEntityId = tenant.Settings.SsoIdentityProviderEntityId,
            SsoMetadataUrl = tenant.Settings.SsoMetadataUrl,
            DefaultLanguage = tenant.Settings.DefaultLanguage
        },
        Database = tenant.Database is not { } db ? null : new PlatformTenantDatabaseDto
        {
            Mode = db.Mode.ToString(),
            DatabaseName = db.DatabaseName,
            DatabaseServerId = db.DatabaseServerId,
            ConnectionStringName = db.ConnectionStringName,
            SchemaVersion = db.SchemaVersion,
            LastMigratedAtUtc = db.LastMigratedAtUtc,
            VerifiedAtUtc = db.VerifiedAtUtc,
            RuntimePrincipal = db.RuntimePrincipal,
            LastMigrationError = db.LastMigrationError
        },
        Subscription = tenant.Subscription is not { } sub ? null : new PlatformTenantSubscriptionDto
        {
            Id = sub.Id,
            PlanCode = sub.Plan.Code,
            PlanNameEn = sub.Plan.NameEn,
            PlanNameAr = sub.Plan.NameAr,
            Status = sub.Status.ToString(),
            StartedAtUtc = sub.StartedAtUtc,
            TrialEndsAtUtc = sub.TrialEndsAtUtc,
            PeriodEndsAtUtc = sub.CurrentPeriodEndsAtUtc,
            Notes = sub.Notes,
            MaxUsers = sub.Plan.MaxUsers,
            MaxTeams = sub.Plan.MaxTeams,
            MaxStorageGb = sub.Plan.MaxStorageGb
        }
    };

    /// <summary>
    /// Makes a catalog change visible on this node at once (other nodes within one refresh).
    /// </summary>
    public static void Publish(ITenantStore tenantStore) => tenantStore.Invalidate();
}
