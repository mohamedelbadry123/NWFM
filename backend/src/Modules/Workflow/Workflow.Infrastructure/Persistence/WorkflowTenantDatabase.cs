namespace Workflow.Infrastructure.Persistence;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NWFM.Shared.MultiTenancy;

/// <summary>
/// The Workflow part of every tenant database: EF migrations and the tenant reference row every
/// workflow aggregate's <c>OrganizationId</c> points at. Its id is the catalog id of the tenant.
/// </summary>
public sealed class WorkflowTenantDatabase : ITenantDatabaseModule
{
    private static readonly Lazy<string?> Latest = new(() =>
        typeof(WorkflowDbContext).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(WorkflowDbContext))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Max(StringComparer.Ordinal));

    public string Name => "Workflow";

    public int Order => 40;

    public string? LatestMigrationId => Latest.Value;

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        [.. await services.GetRequiredService<WorkflowDbContext>().Database.GetPendingMigrationsAsync(cancellationToken)];

    public async Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().Current ?? throw TenantAccessException.Required();
        var db = services.GetRequiredService<WorkflowDbContext>();
        if (!await db.Set<Tenant>().AnyAsync(t => t.Id == tenant.Id, cancellationToken))
        {
            db.Add(new Tenant { Id = tenant.Id, Name = tenant.Name });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
