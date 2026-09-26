using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tasks.Domain.Constants;

namespace Tasks.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>. The connection string is only used to build
/// the model; migrations are generated against it without connecting.
/// </summary>
public sealed class TasksDbContextFactory : IDesignTimeDbContextFactory<TasksDbContext>
{
    private const string DesignTimeConnection =
        "Server=localhost;Database=NWFM;Integrated Security=True;TrustServerCertificate=True";

    public TasksDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TasksDbContext>();

        optionsBuilder.UseSqlServer(
            DesignTimeConnection,
            sql => sql.MigrationsHistoryTable(TasksSchema.MigrationsHistoryTable, TasksSchema.Name));

        return new TasksDbContext(optionsBuilder.Options);
    }
}
