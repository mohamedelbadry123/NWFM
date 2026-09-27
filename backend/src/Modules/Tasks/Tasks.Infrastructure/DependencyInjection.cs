using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NWFM.Shared.Options;
using NWFM.Shared.Persistence;
using Tasks.Application.C2m;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Infrastructure.C2m;
using Tasks.Infrastructure.Persistence;
using Tasks.Infrastructure.Reports;

namespace Tasks.Infrastructure;

public static class DependencyInjection
{
    public static void AddTasksInfrastructure(this IHostApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddDbContext<TasksDbContext>(options => options
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(TasksSchema.MigrationsHistoryTable, TasksSchema.Name))
            // A task's timeline is its audit trail, so nothing may rewrite a line of it.
            .AddInterceptors(new ImmutableEntityInterceptor()));

        builder.Services.AddScoped<ITasksDbContext>(sp => sp.GetRequiredService<TasksDbContext>());
        builder.Services.AddScoped<TaskAccess>();
        builder.Services.AddSingleton<ITaskReportRenderer, TaskReportRenderer>();

        // C2M closure of field activities — off unless C2m:Enabled; see C2mOptions.
        var c2mSection = builder.Configuration.GetSection(C2mOptions.SectionName);
        builder.Services.Configure<C2mOptions>(c2mSection);
        var c2mSettings = c2mSection.Get<C2mOptions>() ?? new C2mOptions();
        builder.Services.AddHttpClient<IC2mClient, C2mHttpClient>(client => C2mHttpClient.Configure(client, c2mSettings));
        builder.Services.AddScoped<IC2mActionMappingResolver, C2mActionMappingResolver>();
        builder.Services.AddScoped<IC2mDispatcher, C2mDispatcher>();
        builder.Services.AddScoped<TaskC2mClosure>();
        builder.Services.AddHostedService<C2mClosureHostedService>();

        builder.Services.Configure<DatabaseStartupOptions>(
            builder.Configuration.GetSection(DatabaseStartupOptions.SectionName));

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddScoped<TasksDatabaseInitializer>();
    }

    /// <summary>
    /// Runs the Tasks database initialiser (migrate, seed) using the three-flag
    /// <see cref="DatabaseStartupOptions"/> pattern. Call after the FormEngine initialiser: the seed
    /// binds its task types to the forms that one publishes.
    /// </summary>
    public static async Task InitialiseTasksDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<TasksDatabaseInitializer>();
        await initialiser.InitialiseAsync();
    }
}
