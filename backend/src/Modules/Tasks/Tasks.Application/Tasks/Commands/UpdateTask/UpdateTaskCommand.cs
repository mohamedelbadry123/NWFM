using FluentValidation;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Common;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.UpdateTask;

/// <summary>
/// Corrects a task. Every field is replaced, not patched, so the client sends the task as it wants
/// it. Details can be corrected until the task closes; the location only until it is filled.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record UpdateTaskCommand : IRequest<Result>, ITaskLocationInput
{
    public Guid TaskId { get; init; }
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public string Priority { get; init; } = TaskPriorities.Normal;
    public string? ExternalReference { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? Address { get; init; }
    public string? CbuCode { get; init; }
    public string? BranchCode { get; init; }
    public string? OperationAreaCode { get; init; }
    public string? DepartmentCode { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
}

public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(FieldTask.TitleMaxLength);
        RuleFor(x => x.Notes).MaximumLength(FieldTask.NotesMaxLength);
        RuleFor(x => x.ExternalReference).MaximumLength(FieldTask.ExternalReferenceMaxLength);
        RuleFor(x => x.Priority).Must(TaskPriorities.IsDefined).WithMessage("Unknown task priority.");
        Include(new TaskLocationValidator());
    }
}

public sealed class UpdateTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateTaskCommand, Result>
{
    public async Task<Result> Handle(UpdateTaskCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        var location = TaskLocationValidator.ToLocation(request, request.DepartmentCode);
        var moved = HasMoved(task, location);

        if (moved && !await access.CoversAsync(location.CbuCode, location.BranchCode, location.OperationAreaCode, location.DepartmentCode, ct))
        {
            return Result.Failure(TaskErrors.Task.OutsideScope);
        }

        var actor = TaskWrites.Actor(user);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return await TaskWrites.ApplyAsync(db, () =>
        {
            task.UpdateDetails(
                request.Title,
                request.Notes,
                request.Priority,
                request.ExternalReference,
                request.DueDate,
                request.CompletionDueDate,
                actor,
                now);

            // Only a real move is refused on a filled task; re-sending the same place is not a move.
            if (moved)
            {
                task.Relocate(location, actor, now);
            }
        }, ct);
    }

    private static bool HasMoved(FieldTask task, TaskLocation location) =>
        Math.Abs(task.Latitude - location.Latitude) > 1e-9
        || Math.Abs(task.Longitude - location.Longitude) > 1e-9
        || !SameCode(task.Address, location.Address)
        || !SameCode(task.CbuCode, location.CbuCode)
        || !SameCode(task.BranchCode, location.BranchCode)
        || !SameCode(task.OperationAreaCode, location.OperationAreaCode)
        || !SameCode(task.DepartmentCode, location.DepartmentCode);

    private static bool SameCode(string? current, string? requested) =>
        string.Equals(
            string.IsNullOrWhiteSpace(current) ? null : current.Trim(),
            string.IsNullOrWhiteSpace(requested) ? null : requested.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
