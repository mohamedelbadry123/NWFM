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

namespace Tasks.Application.Tasks.Commands.TaskForms;

/// <summary>
/// Adds a form to one task, on top of its type's, pinned at its version published now. The same team
/// fills it; the task waits for it like any other of its forms.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record AttachTaskFormCommand : IRequest<Result>
{
    public Guid TaskId { get; init; }
    public Guid FormDefinitionId { get; init; }
}

/// <summary>Removes a form that was added to one task, before the task is filled.</summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record DetachTaskFormCommand(Guid TaskId, Guid FormDefinitionId) : IRequest<Result>;

public sealed class AttachTaskFormCommandValidator : AbstractValidator<AttachTaskFormCommand>
{
    public AttachTaskFormCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Choose the form to add.");
    }
}

public sealed class AttachTaskFormCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<AttachTaskFormCommand, Result>
{
    public async Task<Result> Handle(AttachTaskFormCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        if (task.FormOf(request.FormDefinitionId) is not null)
        {
            return Result.Failure(TaskErrors.Form.AlreadyOnTask);
        }

        var form = await forms.FindPublishedAsync(request.FormDefinitionId, ct);
        if (form is null)
        {
            return Result.Failure(TaskErrors.Form.NotFound);
        }

        if (!form.AcceptsSubmissions)
        {
            return Result.Failure(TaskErrors.Form.NotPublished);
        }

        return await TaskWrites.ApplyAsync(
            db,
            () => task.AttachForm(form.Id, form.CurrentVersionNo, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime),
            ct);
    }
}

public sealed class DetachTaskFormCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<DetachTaskFormCommand, Result>
{
    public async Task<Result> Handle(DetachTaskFormCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        if (task.FormOf(request.FormDefinitionId) is null)
        {
            return Result.Failure(TaskErrors.Form.NotOnTask);
        }

        return await TaskWrites.ApplyAsync(
            db,
            () => task.DetachForm(request.FormDefinitionId, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime),
            ct);
    }
}
