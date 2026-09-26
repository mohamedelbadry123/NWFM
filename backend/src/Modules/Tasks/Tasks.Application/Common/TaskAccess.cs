using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Entities;

namespace Tasks.Application.Common;

/// <summary>
/// The one way a handler reaches tasks: already narrowed to what the caller may see. A task outside
/// the caller's territory — or, for a crew, not handed to its team — is reported as not found, so
/// its existence is not disclosed either.
/// </summary>
public sealed class TaskAccess(ITasksDbContext db, IOrgScopeProvider scopes, ICurrentUser user)
{
    private OrgScopeSet? _scope;

    /// <summary>The caller's team, when the caller is a crew login.</summary>
    public Guid? CallerTeamId => user.TeamId;

    /// <summary>Read once per request: several checks in one command must agree.</summary>
    public async Task<OrgScopeSet> CallerScopeAsync(CancellationToken ct) =>
        _scope ??= await scopes.GetCurrentUserScopeAsync(ct);

    /// <summary>Every task the caller may see, as a query to narrow further.</summary>
    public async Task<IQueryable<FieldTask>> VisibleAsync(CancellationToken ct)
    {
        var query = db.Tasks.AsQueryable().Where(TaskScopeFilter.ForScope(await CallerScopeAsync(ct)));

        return CallerTeamId is Guid teamId
            ? query.Where(TaskScopeFilter.ForTeam(teamId))
            : query;
    }

    /// <summary>A task to change, tracked, with its assignments; null when missing or not the caller's to see.</summary>
    public async Task<FieldTask?> FindForUpdateAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.Tasks
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        return task is not null && TaskScopeFilter.Allows(task, await CallerScopeAsync(ct), CallerTeamId)
            ? task
            : null;
    }

    /// <summary>A task to read, untracked, with its assignments; null when missing or not the caller's to see.</summary>
    public async Task<FieldTask?> FindAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        return task is not null && TaskScopeFilter.Allows(task, await CallerScopeAsync(ct), CallerTeamId)
            ? task
            : null;
    }

    /// <summary>Whether the caller's territory reaches a place — asked before a task is raised or moved there.</summary>
    public async Task<bool> CoversAsync(
        string? cbuCode,
        string? branchCode,
        string? operationAreaCode,
        string? departmentCode,
        CancellationToken ct) =>
        (await CallerScopeAsync(ct)).Covers(cbuCode, branchCode, operationAreaCode, departmentCode);
}
