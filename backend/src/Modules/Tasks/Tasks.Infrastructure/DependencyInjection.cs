using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NWFM.Shared.Options;
using NWFM.Shared.Persistence;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Infrastructure.Persistence;

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
