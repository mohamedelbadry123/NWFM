using System.Reflection;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Domain.Constants;
using FormEngine.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Persistence;

namespace FormEngine.Infrastructure.Persistence;

/// <summary>
/// The FormEngine part of every tenant database: EF migrations, the per-form submission tables that
/// live outside the EF model, and the example forms (each published through the normal publish path).
/// Runs after Auth and before Tasks, whose seeded task types bind to these forms.
/// </summary>
public sealed class FormEngineTenantDatabase(ILogger<FormEngineTenantDatabase> logger) : ITenantDatabaseModule
{
    private static readonly Lazy<string?> Latest = new(() =>
        typeof(FormEngineDbContext).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(FormEngineDbContext))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Max(StringComparer.Ordinal));

    public string Name => "FormEngine";

    public int Order => 20;

    public string? LatestMigrationId => Latest.Value;

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<FormEngineDbContext>();

        // History first: the module moved from the FE schema to FormEngine, and EF has to find the
        // migrations already applied before it can run the one that moves the tables.
        await MigrationHistorySchemaMove.RunAsync(
            context,
            FormEngineSchema.PreviousName,
            FormEngineSchema.Name,
            FormEngineSchema.MigrationsHistoryTable,
            context.Database.ExecuteSqlRawAsync,
            cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        // Submission tables are deliberately outside the EF model: each grows a column per published
        // field. Publishing creates them; this repairs a form published before it had a table of its
        // own, or a database restored without the native tables.
        await EnsureSubmissionTablesAsync(services, context, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<FormEngineDbContext>();
        return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
    }

    public Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken) =>
        FormSeedData.SeedAsync(services.GetRequiredService<IServiceScopeFactory>(), logger, cancellationToken);

    /// <summary>
    /// One scope per form: repairing touches the change tracker and a table, so a failure on one form
    /// must not leave half-applied state for the next. Scopes inherit the ambient tenant.
    /// </summary>
    private async Task EnsureSubmissionTablesAsync(IServiceProvider services, FormEngineDbContext context, CancellationToken ct)
    {
        var published = await context.FormDefinitions
            .AsNoTracking()
            .Where(f => f.CurrentVersionNo != null)
            .Select(f => new { f.Id, f.Code })
            .ToListAsync(ct);

        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        foreach (var form in published)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<IFormPublisher>().EnsureStorageAsync(form.Id, ct);
                if (result.IsFailure)
                {
                    logger.LogError(
                        "Could not ensure the submission table for form {Code}: {ErrorCode} {Message}",
                        form.Code, result.Error.Code, result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not ensure the submission table for form {Code}.", form.Code);
            }
        }
    }
}
