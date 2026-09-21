using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Common;
using Tasks.Application.Tasks.Models;

namespace Tasks.Application.Tasks.Queries.GetTaskById;

/// <summary>One task, with its assignments and the pinned form it is filled with.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskByIdQuery(Guid TaskId) : IRequest<Result<TaskDetailDto>>;

public sealed class GetTaskByIdQueryHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgDirectory directory,
    IFormGateway forms)
    : IRequestHandler<GetTaskByIdQuery, Result<TaskDetailDto>>
{
    public async Task<Result<TaskDetailDto>> Handle(GetTaskByIdQuery request, CancellationToken ct)
    {
        var task = await access.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<TaskDetailDto>(TaskErrors.Task.NotFound);
        }

        var type = await db.TaskTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == task.TaskTypeId, ct);

        var teamIds = task.Assignments.Select(a => a.TeamId).Distinct().ToList();
        var teams = await directory.GetTeamsAsync(teamIds, ct);

        var form = await forms.FindPublishedAsync(task.FormDefinitionId, ct);
        var schemaJson = await forms.GetVersionSchemaAsync(task.FormDefinitionId, task.FormVersionNo, ct);

        var teamName = task.ActiveAssignment is { } active && teams.TryGetValue(active.TeamId, out var activeTeam)
            ? activeTeam.Name
            : null;

        return Result.Success(new TaskDetailDto
        {
            Task = TaskProjection.ToListItem(
                task,
                type,
                teamName,
                form is { AcceptsSubmissions: true } ? form.CurrentVersionNo : null),
            Notes = task.Notes,
            FillSlaHours = task.FillSlaHours,
            CompletionSlaHours = task.CompletionSlaHours,
            AssignedBy = task.AssignedBy,
            LastFilledBy = task.LastFilledBy,
            CompletedBy = task.CompletedBy,
            CompletedDate = task.CompletedDate,
            ReturnedBy = task.ReturnedBy,
            ExpiredBy = task.ExpiredBy,
            ExpiredDate = task.ExpiredDate,
            CreatedBy = task.CreatedBy,
            FormCode = form?.Code,
            FormNameEn = form?.NameEn,
            FormNameAr = form?.NameAr,
            SchemaJson = schemaJson,
            Assignments = task.Assignments
                .OrderByDescending(a => a.AssignedDate)
                .Select(a => new TaskAssignmentDto
                {
                    Id = a.Id,
                    TeamId = a.TeamId,
                    TeamName = teams.TryGetValue(a.TeamId, out var team) ? team.Name : null,
                    Status = a.Status,
                    AssignedBy = a.AssignedBy,
                    AssignedDate = a.AssignedDate,
                    DueDate = a.DueDate,
                    SubmittedDate = a.SubmittedDate,
                    Note = a.Note,
                    IsActive = a.IsActive,
                })
                .ToList(),
        });
    }
}
