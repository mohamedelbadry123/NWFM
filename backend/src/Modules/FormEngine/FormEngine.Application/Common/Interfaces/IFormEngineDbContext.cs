using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FormEngine.Application.Common.Interfaces;

public interface IFormEngineDbContext
{
    DbSet<FormDefinition> FormDefinitions { get; }
    DbSet<FormVersion> FormVersions { get; }
    DbSet<FormField> FormFields { get; }
    DbSet<SubmissionFile> SubmissionFiles { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts a transaction, or returns null when one is already open — the caller then runs inside
    /// it and leaves commit to whoever opened it. The native submission store joins whichever is open.
    /// </summary>
    Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken ct = default);
}
