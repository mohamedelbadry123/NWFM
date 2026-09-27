using FormEngine.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FormEngine.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>. The connection string is only used to build
/// the model; migrations are generated against it without connecting.
/// </summary>
public sealed class FormEngineDbContextFactory : IDesignTimeDbContextFactory<FormEngineDbContext>
{
    private const string DesignTimeConnection =
        "Server=localhost;Database=NWFM;Integrated Security=True;TrustServerCertificate=True";

    public FormEngineDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FormEngineDbContext>();

        optionsBuilder.UseSqlServer(
            DesignTimeConnection,
            sql => sql.MigrationsHistoryTable(FormEngineSchema.MigrationsHistoryTable, FormEngineSchema.Name));

        return new FormEngineDbContext(optionsBuilder.Options);
    }
}
