using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Models;
using Tasks.Domain.Constants;

namespace Tasks.Application.Tasks.Queries.GetTaskActivity;

/// <summary>Everything that has happened to a task, oldest first.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskTimelineQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskHistoryDto>>>;

/// <summary>Every fill recorded for a task, newest first — the first is what the task currently holds.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskFillsQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskFillDto>>>;

/// <summary>The media the task's fills carry.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskFilesQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskFileDto>>>;

public sealed class GetTaskTimelineQueryHandler(ITasksDbContext db, TaskAccess access)
    : IRequestHandler<GetTaskTimelineQuery, Result<IReadOnlyList<TaskHistoryDto>>>
{
    public async Task<Result<IReadOnlyList<TaskHistoryDto>>> Handle(GetTaskTimelineQuery request, CancellationToken ct)
    {
        if (await access.FindAsync(request.TaskId, ct) is null)
        {
            return Result.Failure<IReadOnlyList<TaskHistoryDto>>(TaskErrors.Task.NotFound);
        }

        IReadOnlyList<TaskHistoryDto> history = await db.TaskStatusHistory
            .AsNoTracking()
            .Where(h => h.FieldTaskId == request.TaskId)
            .OrderBy(h => h.ChangedDate)
            .ThenBy(h => h.CreatedAt)
            .Select(h => new TaskHistoryDto(h.Id, h.FromStatus, h.ToStatus, h.ChangedBy, h.ChangedDate, h.Note))
            .ToListAsync(ct);

        return Result.Success(history);
    }
}

public sealed class GetTaskFillsQueryHandler(TaskAccess access, IFormGateway forms)
    : IRequestHandler<GetTaskFillsQuery, Result<IReadOnlyList<TaskFillDto>>>
{
    public async Task<Result<IReadOnlyList<TaskFillDto>>> Handle(GetTaskFillsQuery request, CancellationToken ct)
    {
        var task = await access.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<IReadOnlyList<TaskFillDto>>(TaskErrors.Task.NotFound);
        }

        // The fills live in the pinned form's own table, filed under this task's id.
        var records = await forms.ListByContextAsync(
            task.FormDefinitionId,
            TasksSchema.FormContextType,
            task.Id.ToString("D"),
            ct);

        IReadOnlyList<TaskFillDto> fills = records
            .Select(r => new TaskFillDto(r.SubmissionId, r.VersionNo, r.SubmittedBy, r.SubmittedByName, r.SubmittedDate, r.Answers))
            .ToList();

        return Result.Success(fills);
    }
}

public sealed class GetTaskFilesQueryHandler(TaskAccess access, IFormGateway forms)
    : IRequestHandler<GetTaskFilesQuery, Result<IReadOnlyList<TaskFileDto>>>
{
    public async Task<Result<IReadOnlyList<TaskFileDto>>> Handle(GetTaskFilesQuery request, CancellationToken ct)
    {
        var task = await access.FindAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<IReadOnlyList<TaskFileDto>>(TaskErrors.Task.NotFound);
        }

        var files = await forms.ListFilesByContextAsync(TasksSchema.FormContextType, task.Id.ToString("D"), ct);

        IReadOnlyList<TaskFileDto> result = files
            .Select(f => new TaskFileDto(f.FileId, f.SubmissionId, f.DataName, f.FileName, f.ContentType, f.SizeBytes, f.Status, f.CreatedAt))
            .ToList();

        return Result.Success(result);
    }
}
