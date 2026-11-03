using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Persistence;

/// <summary>
/// The master catalog database. Its own schema and migrations history table, so it can share a SQL
/// Server instance with tenant databases without either side ever seeing the other's tables.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options), ICatalogDbContext
{
    public const string Schema = "catalog";
    public const string MigrationsHistoryTable = "__CatalogMigrationsHistory";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantHost> TenantHosts => Set<TenantHost>();

    public DbSet<TenantDatabase> TenantDatabases => Set<TenantDatabase>();

    public DbSet<DatabaseServer> DatabaseServers => Set<DatabaseServer>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<SignupRequest> SignupRequests => Set<SignupRequest>();

    public DbSet<SignupProvisioningEvent> SignupProvisioningEvents => Set<SignupProvisioningEvent>();

    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();

    public DbSet<SupportSession> SupportSessions => Set<SupportSession>();

    public DbSet<PlatformAuditEntry> PlatformAuditEntries => Set<PlatformAuditEntry>();

    public DbSet<CatalogDataProtectionKey> DataProtectionKeys => Set<CatalogDataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            type => type.Namespace == typeof(CatalogDbContext).Namespace + ".Configurations");
    }
}

/// <summary>An ASP.NET Core Data Protection key ring entry, stored so every node shares one ring.</summary>
public sealed class CatalogDataProtectionKey
{
    public int Id { get; set; }

    public string FriendlyName { get; set; } = default!;

    public string Xml { get; set; } = default!;
}
