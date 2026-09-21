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
/// 2. ApplySqlObjects → make sure every published form has its submission table, registry and columns
/// 3. SeedData        → the example forms, each published through the normal publish path
/// </summary>
public sealed class FormEngineDatabaseInitializer(
    ILogger<FormEngineDatabaseInitializer> logger,
    FormEngineDbContext context,
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
                // Submission tables are deliberately outside the EF model: each grows a column per
                // published field. Publishing creates them; this repairs a form published before it had
                // a table of its own, or a database restored without the native tables.
                logger.LogInformation("Ensuring per-form submission tables (DatabaseStartup:ApplySqlObjects=true).");
                await EnsureSubmissionTablesAsync(ct);
                logger.LogInformation("Per-form submission tables are present.");
            }
            else
            {
                logger.LogInformation("Skipping the submission table check (DatabaseStartup:ApplySqlObjects=false).");
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

    /// <summary>
    /// One scope per form: repairing touches the change tracker and a table, so a failure on one form
    /// must not leave half-applied state for the next — and must not stop the API starting.
    /// </summary>
    private async Task EnsureSubmissionTablesAsync(CancellationToken ct)
    {
        var publishedIds = await context.FormDefinitions
            .AsNoTracking()
            .Where(f => f.CurrentVersionNo != null)
            .Select(f => new { f.Id, f.Code })
            .ToListAsync(ct);

        foreach (var form in publishedIds)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<IFormPublisher>();

                var result = await publisher.EnsureStorageAsync(form.Id, ct);
                if (result.IsFailure)
                {
                    logger.LogError(
                        "Could not ensure the submission table for form {Code}: {ErrorCode} {Message}",
                        form.Code,
                        result.Error.Code,
                        result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not ensure the submission table for form {Code}.", form.Code);
            }
        }
    }
}
