using System.Reflection;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FormEngine.Infrastructure.Persistence;

/// <summary>
/// The FormEngine module's own context, in the <c>FE</c> schema. It does not map
/// <c>FE.Submissions</c>: that table grows a column per published field, so it is created and
/// widened by native SQL instead of by a migration (see <c>IFormSubmissionStore</c>).
/// </summary>
public sealed class FormEngineDbContext(DbContextOptions<FormEngineDbContext> options)
    : DbContext(options), IFormEngineDbContext
{
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormVersion> FormVersions => Set<FormVersion>();
    public DbSet<FieldCatalogEntry> FieldCatalog => Set<FieldCatalogEntry>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();

    public async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken ct = default) =>
        Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(ct)
            : null;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(FormEngineSchema.Name);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
