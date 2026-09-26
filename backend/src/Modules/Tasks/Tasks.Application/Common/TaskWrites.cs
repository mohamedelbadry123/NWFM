using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;

namespace Tasks.Application.Common;

/// <summary>What every task command does around its change: name the actor, apply, save.</summary>
internal static class TaskWrites
{
    /// <summary>
    /// The name stamped on history and assignments. Tasks never reads Auth's users, so the timeline
    /// keeps the name as it was when the change was made rather than an id to resolve later.
    /// </summary>
    public static string? Actor(ICurrentUser user) => user.UserName ?? user.Id;

    /// <summary>
    /// Applies a domain change and saves it. A refused transition becomes a 409 carrying the entity's
    /// own reason; a row someone else changed first becomes a concurrency conflict, not a 500.
    /// </summary>
    public static async Task<Result> ApplyAsync(ITasksDbContext db, Action change, CancellationToken ct)
    {
        try
        {
            change();
        }
        catch (DomainException ex)
        {
            return Result.Failure(TaskErrors.Task.Invalid(ex.Message));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(TaskErrors.Task.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
