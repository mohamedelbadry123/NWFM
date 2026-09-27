using FluentValidation;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.Tasks.Models;
using Tasks.Domain.Constants;

namespace Tasks.Application.Tasks.Commands.SubmitTaskFill;

/// <summary>
/// Records a fill of the task's form. The answers are stored in the pinned form's own submission
/// table, filed under this task; the task then moves to SUBMITTED.
///
/// Two modules write here — the form engine stores the answers, Tasks updates the task — so the
/// client's key is what holds them together. It is generated once when the fill dialog opens and
/// sent on every retry: if the task update fails after the answers were stored, the retry is
/// answered with the stored submission and completes the task update, instead of filling twice.
/// </summary>
[Authorize(Policy = NwfmPolicies.SubmitTasks)]
public sealed record SubmitTaskFillCommand : IRequest<Result<TaskFillResultDto>>
{
    public Guid TaskId { get; init; }
    public Guid? ClientSubmissionId { get; init; }
    public DateTimeOffset? ClientFilledAt { get; init; }
    public Dictionary<string, object?> Answers { get; init; } = [];
}

public sealed class SubmitTaskFillCommandValidator : AbstractValidator<SubmitTaskFillCommand>
{
    public SubmitTaskFillCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Answers).NotNull().WithMessage("Answers are required.");
    }
}

public sealed class SubmitTaskFillCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<SubmitTaskFillCommand, Result<TaskFillResultDto>>
{
    public async Task<Result<TaskFillResultDto>> Handle(SubmitTaskFillCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<TaskFillResultDto>(TaskErrors.Task.NotFound);
        }

        // Checked before the answers are stored, so a fill the task cannot take leaves nothing behind
        // in the form's table.
        if (TaskStatuses.IsClosed(task.Status))
        {
            return Result.Failure<TaskFillResultDto>(TaskErrors.Task.Invalid($"An {task.Status} task cannot be filled."));
        }

        // A crew fills the work it holds now, not work that has since moved to another crew.
        if (access.CallerTeamId is Guid teamId && task.ActiveAssignment?.TeamId != teamId)
        {
            return Result.Failure<TaskFillResultDto>(TaskErrors.Task.Invalid("This task is not assigned to your team."));
        }

        var stored = await forms.SubmitAsync(
            new FormSubmitRequest
            {
                FormId = task.FormDefinitionId,
                VersionNo = task.FormVersionNo,
                ContextType = TasksSchema.FormContextType,
                ContextId = task.Id.ToString("D"),
                ClientSubmissionId = request.ClientSubmissionId,
                ClientFilledAt = request.ClientFilledAt,
                Answers = request.Answers,
            },
            ct);

        if (stored.IsFailure)
        {
            // The form engine's own error: which answer is wrong, or why the form will not take it.
            return Result.Failure<TaskFillResultDto>(stored.Error);
        }

        var receipt = stored.Value;

        var applied = await TaskWrites.ApplyAsync(
            db,
            () => task.RecordFill(receipt.SubmissionId, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime),
            ct);

        return applied.IsFailure
            ? Result.Failure<TaskFillResultDto>(applied.Error)
            : Result.Success(new TaskFillResultDto(receipt.SubmissionId, receipt.VersionNo, receipt.IsReplay, task.Status));
    }
}
