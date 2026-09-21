using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Submissions.Common;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Options;
using FormEngine.Infrastructure.Integration;
using FormEngine.Infrastructure.Persistence;
using FormEngine.Infrastructure.Submissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Options;
using NWFM.Shared.Persistence;
using NWFM.Shared.Storage;

namespace FormEngine.Infrastructure;

public static class DependencyInjection
{
    public static void AddFormEngineInfrastructure(
        this IHostApplicationBuilder builder,
        string connectionString)
    {
        builder.Services.AddDbContext<FormEngineDbContext>(options => options
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(FormEngineSchema.MigrationsHistoryTable, FormEngineSchema.Name))
            // Published versions are what pinned consumers resolve against, so nothing may rewrite one.
            .AddInterceptors(new ImmutableEntityInterceptor()));

        builder.Services.AddScoped<IFormEngineDbContext>(sp => sp.GetRequiredService<FormEngineDbContext>());

        builder.Services.Configure<FormEngineOptions>(
            builder.Configuration.GetSection(FormEngineOptions.SectionName));
        builder.Services.Configure<FileStorageOptions>(
            builder.Configuration.GetSection(FileStorageOptions.SectionName));
        builder.Services.Configure<DatabaseStartupOptions>(
            builder.Configuration.GetSection(DatabaseStartupOptions.SectionName));

        // Shared with any module that later needs to store a file.
        builder.Services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Holds the reloadable SQL text for the per-form submission tables.
        builder.Services.AddSingleton<SqlStatementStore>();
        builder.Services.AddScoped<IFormSubmissionStore, FormSubmissionStore>();
        builder.Services.AddScoped<IFormPublisher, FormPublisher>();
        builder.Services.AddScoped<IFormSubmissionService, FormSubmissionService>();

        // How other modules — Tasks, a workflow task screen — read and fill forms without referencing this one.
        builder.Services.AddScoped<IFormGateway, FormGateway>();

        builder.Services.AddScoped<FormEngineDatabaseInitializer>();
    }

    /// <summary>
    /// Runs the FormEngine database initialiser (migrate, submission tables, seed) using the
    /// three-flag <see cref="DatabaseStartupOptions"/> pattern. Call after <c>builder.Build()</c>,
    /// before <c>app.Run()</c>.
    /// </summary>
    public static async Task InitialiseFormEngineDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<FormEngineDatabaseInitializer>();
        await initialiser.InitialiseAsync();
    }
}
