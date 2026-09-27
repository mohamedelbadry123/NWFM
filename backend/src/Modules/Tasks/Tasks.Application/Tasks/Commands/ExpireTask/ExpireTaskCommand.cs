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
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.ExpireTask;

/// <summary>Closes a task without approving it — withdrawn, duplicated, no longer needed.</summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record ExpireTaskCommand : IRequest<Result>
{
    public Guid TaskId { get; init; }
    public string? Note { get; init; }
}

public sealed class ExpireTaskCommandValidator : AbstractValidator<ExpireTaskCommand>
{
    public ExpireTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(TaskStatusHistory.NoteMaxLength);
    }
}

public sealed class ExpireTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<ExpireTaskCommand, Result>
{
    public async Task<Result> Handle(ExpireTaskCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        return await TaskWrites.ApplyAsync(
            db,
            () => task.Expire(TaskWrites.Actor(user), request.Note, timeProvider.GetUtcNow().UtcDateTime),
            ct);
    }
}

/// <summary>
/// Moves the task's unfilled forms to their current published versions — one form when
/// <see cref="FormDefinitionId"/> names it, else every unfilled form a newer version has overtaken.
/// Answers how many forms moved.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record MigrateTaskFormVersionCommand(Guid TaskId, Guid? FormDefinitionId = null) : IRequest<Result<int>>;

public sealed class MigrateTaskFormVersionCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<MigrateTaskFormVersionCommand, Result<int>>
{
    public async Task<Result<int>> Handle(MigrateTaskFormVersionCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<int>(TaskErrors.Task.NotFound);
        }

        if (request.FormDefinitionId is Guid formId)
        {
            if (task.FormOf(formId) is null)
            {
                return Result.Failure<int>(TaskErrors.Form.NotOnTask);
            }

            var form = await forms.FindPublishedAsync(formId, ct);
            if (form is not { AcceptsSubmissions: true })
            {
                return Result.Failure<int>(TaskErrors.Form.NotPublished);
            }

            var one = await TaskWrites.ApplyAsync(
                db,
                () => task.MigrateFormVersion(formId, form.CurrentVersionNo, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime),
                ct);

            return one.IsFailure ? Result.Failure<int>(one.Error) : Result.Success(1);
        }

        // Every unfilled form a newer version has overtaken; forms already filled stay on what they answered.
        var moves = new List<(Guid FormId, int VersionNo)>();
        foreach (var taskForm in task.OrderedForms.Where(f => !f.IsFilled))
        {
            var form = await forms.FindPublishedAsync(taskForm.FormDefinitionId, ct);
            if (form is { AcceptsSubmissions: true } && form.CurrentVersionNo > taskForm.FormVersionNo)
            {
                moves.Add((taskForm.FormDefinitionId, form.CurrentVersionNo));
            }
        }

        if (moves.Count == 0)
        {
            return Result.Failure<int>(TaskErrors.Task.Invalid("None of the task's unfilled forms has a newer version to move to."));
        }

        var applied = await TaskWrites.ApplyAsync(
            db,
            () =>
            {
                foreach (var (formId, versionNo) in moves)
                {
                    task.MigrateFormVersion(formId, versionNo, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime);
                }
            },
            ct);

        return applied.IsFailure ? Result.Failure<int>(applied.Error) : Result.Success(moves.Count);
    }
}
