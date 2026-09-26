using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.C2m;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.CompleteTask;

/// <summary>
/// Approves a filled task. When the task settles a C2M field activity, the activity is closed in C2M
/// as part of the approval (see <see cref="C2mOptions.WaitForAcknowledgement"/>).
/// </summary>
[Authorize(Policy = NwfmPolicies.ReviewTasks)]
public sealed record CompleteTaskCommand : IRequest<Result>
{
    public Guid TaskId { get; init; }
    public string? Note { get; init; }
}

public sealed class CompleteTaskCommandValidator : AbstractValidator<CompleteTaskCommand>
{
    public CompleteTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(TaskStatusHistory.NoteMaxLength);
    }
}

public sealed class CompleteTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    TaskC2mClosure c2m,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<CompleteTaskCommand, Result>
{
    public async Task<Result> Handle(CompleteTaskCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        // Checked before anything is sent: C2M must never be told about an approval the task refuses.
        if (task.Status != TaskStatuses.Submitted)
        {
            return Result.Failure(TaskErrors.Task.Invalid($"Only a filled task can be approved (current: {task.Status})."));
        }

        // One timestamp for the approval and the completionDTTM C2M is told, so the two cannot disagree.
        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        var closeInC2m = await c2m.AppliesAsync(task, ct);

        if (closeInC2m && c2m.Options.WaitForAcknowledgement)
        {
            // Before Complete(), deliberately: the task stays as it was until C2M has accepted, so a
            // refusal leaves nothing to roll back. The activity is C2M's, and only C2M can settle it.
            var outcome = await c2m.SendAsync(
                task,
                completedAt,
                TimeSpan.FromSeconds(c2m.Options.AcknowledgementTimeoutSeconds),
                queueOnTransportFailure: false,
                ct);

            if (!outcome.AllowsApproval)
            {
                // Commits the attempt and nothing else, so the next try is recorded as a second attempt.
                await db.SaveChangesAsync(ct);
                return Result.Failure(outcome.Kind == C2mDispatchOutcomeKind.Rejected
                    ? TaskErrors.C2m.Rejected(task.FaId!, outcome.Message, outcome.ResponseCode)
                    : TaskErrors.C2m.Unavailable(task.FaId!, outcome.Message));
            }
        }

        try
        {
            task.Complete(TaskWrites.Actor(user), request.Note, completedAt);

            if (closeInC2m && !c2m.Options.WaitForAcknowledgement)
            {
                // Queued with the approval in one save: a C2M outage must not undo the reviewer's
                // decision, and the background sender picks it up.
                task.QueueC2mClosure();
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(TaskErrors.Task.Invalid(ex.Message));
        }

        try
        {
            // One save: the approval and the accepted closure's record land together, so a task is never
            // APPROVED without the C2M answer that allowed it.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(TaskErrors.Task.ConcurrencyConflict);
        }

        return Result.Success();
    }
}
