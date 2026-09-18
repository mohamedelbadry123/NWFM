using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace NWFM.Shared.Persistence;

public sealed class ImmutableEntityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowIfImmutableEntityModified(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfImmutableEntityModified(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ThrowIfImmutableEntityModified(DbContext? context)
    {
        if (context is null) return;

        var violations = context.ChangeTracker
            .Entries()
            .Where(e => e.Entity is IImmutableEntity &&
                        (e.State == EntityState.Modified || e.State == EntityState.Deleted))
            .Select(e => e.Entity.GetType().Name)
            .ToList();

        if (violations.Count == 0) return;

        throw new DomainException(
            $"Attempted to mutate immutable entities: {string.Join(", ", violations)}. " +
            "These records are append-only per PDPL Art. 9, Art. 20, and NCA ECC requirements.");
    }
}
