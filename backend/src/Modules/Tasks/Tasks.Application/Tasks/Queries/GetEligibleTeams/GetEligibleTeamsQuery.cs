using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Common;
using Tasks.Application.Tasks.Models;

namespace Tasks.Application.Tasks.Queries.GetEligibleTeams;

/// <summary>
/// The teams that may take a task: active, with a territory that covers the task, and within the
/// caller's own territory — a supervisor cannot hand work to a crew they do not oversee. Each comes
/// with the number of tasks it already holds, so the load can be spread.
/// </summary>
[Authorize(Policy = NwfmPolicies.AssignTasks)]
public sealed record GetEligibleTeamsQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<EligibleTeamDto>>>;

public sealed class GetEligibleTeamsQueryHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgDirectory directory)
    : IRequestHandler<GetEligibleTeamsQuery, Result<IReadOnlyList<EligibleTeamDto>>>
{
    public async Task<Result<IReadOnlyList<EligibleTeamDto>>> Handle(GetEligibleTeamsQuery request, CancellationToken ct)
    {
        var task = await access.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<IReadOnlyList<EligibleTeamDto>>(TaskErrors.Task.NotFound);
        }

        var callerScope = await access.CallerScopeAsync(ct);
        var teams = await TeamEligibility.CoveringAsync(directory, callerScope, task, ct);

        var teamIds = teams.Select(t => t.Id).ToList();
        var load = await TeamEligibility.OpenTaskCountsAsync(db, teamIds, ct);

        IReadOnlyList<EligibleTeamDto> result = teams
            .Select(t => new EligibleTeamDto(t.Id, t.Name, t.Mobile, load.GetValueOrDefault(t.Id)))
            .OrderBy(t => t.ActiveTaskCount)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Result.Success(result);
    }
}
