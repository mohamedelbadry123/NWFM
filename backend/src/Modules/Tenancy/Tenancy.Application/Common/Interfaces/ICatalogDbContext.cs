namespace Tenancy.Application.Common.Interfaces;

/// <summary>
/// The master catalog: tenants, plans, subscriptions, signups, platform staff and support grants.
/// Separate database from every tenant; never reachable through a tenant module's DbContext.
/// </summary>
public interface ICatalogDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<TenantHost> TenantHosts { get; }

    DbSet<TenantDatabase> TenantDatabases { get; }

    DbSet<DatabaseServer> DatabaseServers { get; }

    DbSet<Plan> Plans { get; }

    DbSet<Subscription> Subscriptions { get; }

    DbSet<SignupRequest> SignupRequests { get; }

    DbSet<SignupProvisioningEvent> SignupProvisioningEvents { get; }

    DbSet<PlatformUser> PlatformUsers { get; }

    DbSet<SupportAccessGrant> SupportAccessGrants { get; }

    DbSet<SupportSession> SupportSessions { get; }

    DbSet<PlatformAuditEntry> PlatformAuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
