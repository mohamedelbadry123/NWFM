using FluentValidation;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.CompleteTask;

/// <summary>Approves a filled task.</summary>
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

        return await TaskWrites.ApplyAsync(
            db,
            () => task.Complete(TaskWrites.Actor(user), request.Note, timeProvider.GetUtcNow().UtcDateTime),
            ct);
    }
}
