using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tenancy.Infrastructure.Persistence;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Builds the catalog context for <c>dotnet ef</c>. The catalog's migrations live in this assembly
/// (the control plane's); set <c>ConnectionStrings__CatalogConnection</c> to point it elsewhere.
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    private const string DefaultDesignTimeConnection =
        "Server=localhost;Database=NWFM_Catalog;Integrated Security=True;TrustServerCertificate=True";

    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CatalogConnection")
            ?? DefaultDesignTimeConnection;

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer(connectionString, sql => sql
                .MigrationsHistoryTable(CatalogDbContext.MigrationsHistoryTable, CatalogDbContext.Schema)
                .MigrationsAssembly(typeof(CatalogDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new CatalogDbContext(options);
    }
}
