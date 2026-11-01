using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Persistence;
using Tasks.Domain.Constants;
using Tasks.Infrastructure.Persistence.Seed;

namespace Tasks.Infrastructure.Persistence;

/// <summary>
/// The Tasks part of every tenant database. Runs after FormEngine: the seeded task types bind to
/// the forms that module publishes. The example tasks are seeded for the bootstrap workspace only.
/// </summary>
public sealed class TasksTenantDatabase(ILogger<TasksTenantDatabase> logger) : ITenantDatabaseModule
{
    private static readonly Lazy<string?> Latest = new(() =>
        typeof(TasksDbContext).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(TasksDbContext))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Max(StringComparer.Ordinal));

    public string Name => "Tasks";

    public int Order => 30;

    public string? LatestMigrationId => Latest.Value;

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<TasksDbContext>();

        // History first: the module moved from the TK schema to Task, and EF has to find the
        // migrations already applied before it can run the one that moves the tables.
        await MigrationHistorySchemaMove.RunAsync(
            context,
            TasksSchema.PreviousName,
            TasksSchema.Name,
            TasksSchema.MigrationsHistoryTable,
            context.Database.ExecuteSqlRawAsync,
            cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<TasksDbContext>();
        return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
    }

    public Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        TaskSeedData.SeedAsync(
            services.GetRequiredService<IServiceScopeFactory>(), logger, cancellationToken,
            includeExampleTasks: profile == TenantSeedProfile.Bootstrap);
}
