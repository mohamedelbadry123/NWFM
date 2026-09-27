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
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.SubmitTaskFill;

/// <summary>
/// Records a fill of one of the task's forms. The answers are stored in that form's own submission
/// table, filed under this task; the task moves to SUBMITTED once every form it waits for is filled,
/// and to IN_PROGRESS while some still wait.
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

    /// <summary>Which of the task's forms is filled. May be left out only when the task has one.</summary>
    public Guid? FormDefinitionId { get; init; }

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

        var taskForm = request.FormDefinitionId is Guid formId
            ? task.FormOf(formId)
            : task.Forms.Count == 1 ? task.Forms.First() : null;

        if (taskForm is null)
        {
            return Result.Failure<TaskFillResultDto>(
                request.FormDefinitionId is null ? TaskErrors.Form.ChoiceRequired : TaskErrors.Form.NotOnTask);
        }

        var stored = await forms.SubmitAsync(
            new FormSubmitRequest
            {
                FormId = taskForm.FormDefinitionId,
                VersionNo = taskForm.FormVersionNo,
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

        // The form's computed columns, worked out through the version this fill answered — what the
        // task grid shows beside the task until the form is filled again.
        var computed = (await forms.ComputeAsync(taskForm.FormDefinitionId, receipt.VersionNo, request.Answers, ct))
            .Select(c => new TaskComputedValueDraft(c.Key, c.OutputType, c.Number, c.Text))
            .ToList();

        var applied = await TaskWrites.ApplyAsync(
            db,
            () => task.RecordFill(
                taskForm.FormDefinitionId,
                receipt.SubmissionId,
                TaskWrites.Actor(user),
                timeProvider.GetUtcNow().UtcDateTime,
                computed),
            ct);

        return applied.IsFailure
            ? Result.Failure<TaskFillResultDto>(applied.Error)
            : Result.Success(new TaskFillResultDto(
                receipt.SubmissionId,
                taskForm.FormDefinitionId,
                receipt.VersionNo,
                receipt.IsReplay,
                task.Status,
                task.FilledFormCount,
                task.RequiredFormCount));
    }
}
