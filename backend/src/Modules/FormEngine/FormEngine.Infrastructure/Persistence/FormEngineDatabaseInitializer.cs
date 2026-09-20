using FormEngine.Application.Common.Interfaces;
using FormEngine.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace FormEngine.Infrastructure.Persistence;

/// <summary>
/// Three-flag database initializer for the FormEngine module, mirroring the Auth one:
/// 1. ApplyMigrations → EF Core MigrateAsync
/// 2. ApplySqlObjects → create the shared FE.Submissions table and its indexes if missing
/// 3. SeedData        → the example forms, each published through the normal publish path
/// </summary>
public sealed class FormEngineDatabaseInitializer(
    ILogger<FormEngineDatabaseInitializer> logger,
    FormEngineDbContext context,
    IFormSubmissionStore submissionStore,
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseStartupOptions> startupOptions)
{
    private readonly DatabaseStartupOptions _startupOptions = startupOptions.Value;

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        logger.LogInformation(
            "FormEngine DatabaseStartup settings: ApplyMigrations={ApplyMigrations}, ApplySqlObjects={ApplySqlObjects}, SeedData={SeedData}.",
            _startupOptions.ApplyMigrations,
            _startupOptions.ApplySqlObjects,
            _startupOptions.SeedData);

        try
        {
            if (_startupOptions.ApplyMigrations)
            {
                logger.LogInformation("Applying FormEngine EF Core migrations (DatabaseStartup:ApplyMigrations=true).");
                await context.Database.MigrateAsync(ct);
                logger.LogInformation("FormEngine EF Core migrations completed.");
            }
            else
            {
                logger.LogInformation("Skipping FormEngine EF migrations (DatabaseStartup:ApplyMigrations=false).");
            }

            if (_startupOptions.ApplySqlObjects)
            {
                // FE.Submissions is deliberately outside the EF model: it grows a column per published
                // field, so it is created here and extended on every publish.
                logger.LogInformation("Ensuring the shared FE.Submissions table (DatabaseStartup:ApplySqlObjects=true).");
                await submissionStore.EnsureTableAsync(ct);
                logger.LogInformation("Shared FE.Submissions table is present.");
            }
            else
            {
                logger.LogInformation("Skipping the FE.Submissions table check (DatabaseStartup:ApplySqlObjects=false).");
            }

            if (_startupOptions.SeedData)
            {
                logger.LogInformation("Seeding FormEngine example forms (DatabaseStartup:SeedData=true).");
                await FormSeedData.SeedAsync(scopeFactory, logger, ct);
                logger.LogInformation("FormEngine seed completed.");
            }
            else
            {
                logger.LogInformation("Skipping FormEngine seed (DatabaseStartup:SeedData=false).");
            }

            logger.LogInformation("FormEngine database startup initialisation finished.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initialising the FormEngine database.");
            throw;
        }
    }
}
