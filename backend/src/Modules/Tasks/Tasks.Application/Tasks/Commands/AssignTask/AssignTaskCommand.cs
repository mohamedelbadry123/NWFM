using FluentValidation;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Common;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.AssignTask;

/// <summary>
/// Hands an unfilled task to a team. Deadlines left blank are taken from the task type's SLA,
/// counted from now: the fill is due after the fill SLA, review after that plus the completion SLA.
/// </summary>
[Authorize(Policy = NwfmPolicies.AssignTasks)]
public sealed record AssignTaskCommand : IRequest<Result>
{
    public Guid TaskId { get; init; }
    public Guid TeamId { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
    public string? Note { get; init; }
}

public sealed class AssignTaskCommandValidator : AbstractValidator<AssignTaskCommand>
{
    public AssignTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.TeamId).NotEmpty().WithMessage("Choose a team.");
        RuleFor(x => x.Note).MaximumLength(TaskAssignment.NoteMaxLength);
        RuleFor(x => x.CompletionDueDate)
            .GreaterThanOrEqualTo(x => x.DueDate!.Value)
            .When(x => x.DueDate is not null && x.CompletionDueDate is not null)
            .WithMessage("Review cannot be due before the fill is.");
    }
}

public sealed class AssignTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgDirectory directory,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<AssignTaskCommand, Result>
{
    public async Task<Result> Handle(AssignTaskCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        var callerScope = await access.CallerScopeAsync(ct);
        if (!await TeamEligibility.MayTakeAsync(directory, callerScope, task, request.TeamId, ct))
        {
            return Result.Failure(TaskErrors.Team.NotEligible);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (dueDate, completionDueDate) = Deadlines(task, request, now);

        return await TaskWrites.ApplyAsync(
            db,
            () => task.Assign(request.TeamId, TaskWrites.Actor(user), dueDate, completionDueDate, request.Note, now),
            ct);
    }

    /// <summary>What the caller set; otherwise what the task already had; otherwise the SLA from now.</summary>
    private static (DateTime? Due, DateTime? CompletionDue) Deadlines(FieldTask task, AssignTaskCommand request, DateTime now)
    {
        var due = request.DueDate
            ?? task.DueDate
            ?? (task.FillSlaHours is int fillHours ? now.AddHours(fillHours) : null);

        var completionDue = request.CompletionDueDate
            ?? task.CompletionDueDate
            ?? (task.CompletionSlaHours is int completionHours ? (due ?? now).AddHours(completionHours) : null);

        return (due, completionDue);
    }
}
