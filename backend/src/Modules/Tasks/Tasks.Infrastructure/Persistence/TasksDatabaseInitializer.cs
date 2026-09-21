using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using NWFM.Shared.Persistence;
using Tasks.Domain.Constants;
using Tasks.Infrastructure.Persistence.Seed;

namespace Tasks.Infrastructure.Persistence;

/// <summary>
/// Database initializer for the Tasks module, on the same flags as the others:
/// 1. ApplyMigrations → EF Core MigrateAsync
/// 2. SeedData        → example task types and tasks (development)
/// Tasks has no native SQL objects, so <c>ApplySqlObjects</c> has nothing to do here.
/// </summary>
public sealed class TasksDatabaseInitializer(
    ILogger<TasksDatabaseInitializer> logger,
    TasksDbContext context,
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseStartupOptions> startupOptions)
{
    private readonly DatabaseStartupOptions _startupOptions = startupOptions.Value;

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            if (_startupOptions.ApplyMigrations)
            {
                logger.LogInformation("Applying Tasks EF Core migrations (DatabaseStartup:ApplyMigrations=true).");
                // History first: the module moved from the TK schema to Task, and EF has to find
                // the migrations already applied before it can run the one that moves the tables.
                await MigrationHistorySchemaMove.RunAsync(
                    context,
                    TasksSchema.PreviousName,
                    TasksSchema.Name,
                    TasksSchema.MigrationsHistoryTable,
                    context.Database.ExecuteSqlRawAsync,
                    ct);
                await context.Database.MigrateAsync(ct);
            }
            else
            {
                logger.LogInformation("Skipping Tasks EF migrations (DatabaseStartup:ApplyMigrations=false).");
            }

            if (_startupOptions.SeedData)
            {
                try
                {
                    await TaskSeedData.SeedAsync(scopeFactory, logger, ct);
                }
                catch (Exception ex)
                {
                    // Example data: a failed seed is logged and the API still starts.
                    logger.LogError(ex, "Seeding Tasks example data failed.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initialising the Tasks database.");
            throw;
        }
    }
}
