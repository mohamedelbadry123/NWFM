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
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.ReturnTask;

/// <summary>Sends a fill back for rework — to the same crew, or to another that may take the task.</summary>
[Authorize(Policy = NwfmPolicies.ReviewTasks)]
public sealed record ReturnTaskCommand : IRequest<Result>
{
    public Guid TaskId { get; init; }
    public string ReasonCode { get; init; } = default!;
    public string Reason { get; init; } = default!;
    public Guid? ReassignToTeamId { get; init; }
}

public sealed class ReturnTaskCommandValidator : AbstractValidator<ReturnTaskCommand>
{
    public ReturnTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.ReasonCode).Must(TaskReturnReasons.IsDefined).WithMessage("Choose a return reason.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Say what needs doing.")
            .MaximumLength(FieldTask.ReturnReasonMaxLength);
    }
}

public sealed class ReturnTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgDirectory directory,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<ReturnTaskCommand, Result>
{
    public async Task<Result> Handle(ReturnTaskCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure(TaskErrors.Task.NotFound);
        }

        if (request.ReassignToTeamId is Guid teamId
            && teamId != task.ActiveAssignment?.TeamId
            && !await TeamEligibility.MayTakeAsync(directory, await access.CallerScopeAsync(ct), task, teamId, ct))
        {
            return Result.Failure(TaskErrors.Team.NotEligible);
        }

        return await TaskWrites.ApplyAsync(
            db,
            () => task.Return(
                request.ReasonCode,
                request.Reason,
                TaskWrites.Actor(user),
                request.ReassignToTeamId,
                timeProvider.GetUtcNow().UtcDateTime),
            ct);
    }
}
