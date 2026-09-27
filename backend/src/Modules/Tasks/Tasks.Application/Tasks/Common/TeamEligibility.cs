using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Common;

/// <summary>Which teams may take a task. Shared by the eligible-teams list and the assign command, so the two agree.</summary>
internal static class TeamEligibility
{
    /// <summary>
    /// Active teams whose territory covers the task and overlaps the caller's own — a team outside
    /// the caller's territory is not theirs to direct, even if it covers the task.
    /// </summary>
    public static async Task<IReadOnlyList<OrgTeamInfo>> CoveringAsync(
        IOrgDirectory directory,
        OrgScopeSet callerScope,
        FieldTask task,
        CancellationToken ct)
    {
        var teams = await directory.GetActiveTeamsAsync(ct);

        return teams
            .Where(t => t.Scope.Covers(task.CbuCode, task.BranchCode, task.OperationAreaCode, task.DepartmentCode))
            .Where(t => callerScope.Overlaps(t.Scope))
            .Select(t => t.Team)
            .ToList();
    }

    public static async Task<bool> MayTakeAsync(
        IOrgDirectory directory,
        OrgScopeSet callerScope,
        FieldTask task,
        Guid teamId,
        CancellationToken ct) =>
        (await CoveringAsync(directory, callerScope, task, ct)).Any(t => t.Id == teamId);

    /// <summary>Open tasks each team holds now — assigned and not yet approved or expired.</summary>
    public static async Task<Dictionary<Guid, int>> OpenTaskCountsAsync(
        ITasksDbContext db,
        IReadOnlyList<Guid> teamIds,
        CancellationToken ct)
    {
        if (teamIds.Count == 0)
        {
            return [];
        }

        var ids = teamIds.ToList();

        return await db.TaskAssignments
            .AsNoTracking()
            .Where(a => a.IsActive
                && ids.Contains(a.TeamId)
                && a.Status != TaskAssignmentStatuses.Approved
                && a.Status != TaskAssignmentStatuses.Expired)
            .GroupBy(a => a.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, ct);
    }
}
