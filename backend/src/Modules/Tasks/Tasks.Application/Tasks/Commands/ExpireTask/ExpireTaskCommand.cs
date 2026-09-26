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

/// <summary>Moves an unfilled task to its form's current published version.</summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record MigrateTaskFormVersionCommand(Guid TaskId) : IRequest<Result<int>>;

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

        var form = await forms.FindPublishedAsync(task.FormDefinitionId, ct);
        if (form is not { AcceptsSubmissions: true })
        {
            return Result.Failure<int>(TaskErrors.Form.NotPublished);
        }

        var applied = await TaskWrites.ApplyAsync(
            db,
            () => task.MigrateFormVersion(form.CurrentVersionNo, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime),
            ct);

        return applied.IsFailure ? Result.Failure<int>(applied.Error) : Result.Success(task.FormVersionNo);
    }
}
