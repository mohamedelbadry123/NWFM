using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Domain.Constants;

namespace Tasks.Application.C2m;

/// <summary>One attempt to close the task's field activity in C2M.</summary>
public sealed record C2mDispatchLogDto(
    Guid Id,
    int AttemptNumber,
    string FaId,
    string OpStatus,
    string Status,
    string? ResponseCode,
    string? ErrorMessage,
    string RequestJson,
    string? ResponseJson,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>Where a retried closure left the task.</summary>
public sealed record C2mRetryResultDto(string? C2mStatus, string Outcome, string? Message, string? ResponseCode);

/// <summary>Every attempt to close the task's field activity in C2M, newest first.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTaskC2mLogsQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<C2mDispatchLogDto>>>;

public sealed class GetTaskC2mLogsQueryHandler(ITasksDbContext db, TaskAccess access)
    : IRequestHandler<GetTaskC2mLogsQuery, Result<IReadOnlyList<C2mDispatchLogDto>>>
{
    public async Task<Result<IReadOnlyList<C2mDispatchLogDto>>> Handle(GetTaskC2mLogsQuery request, CancellationToken ct)
    {
        if (await access.FindAsync(request.TaskId, ct) is null)
        {
            return Result.Failure<IReadOnlyList<C2mDispatchLogDto>>(TaskErrors.Task.NotFound);
        }

        IReadOnlyList<C2mDispatchLogDto> logs = await db.C2mDispatchLogs
            .AsNoTracking()
            .Where(x => x.TaskId == request.TaskId)
            .OrderByDescending(x => x.AttemptNumber)
            .Select(x => new C2mDispatchLogDto(
                x.Id,
                x.AttemptNumber,
                x.FaId,
                x.OpStatus,
                x.Status,
                x.ResponseCode,
                x.ErrorMessage,
                x.RequestJson,
                x.ResponseJson,
                x.CreatedAt,
                x.CompletedAt))
            .ToListAsync(ct);

        return Result.Success(logs);
    }
}

/// <summary>
/// Sends an approved task's refused or failed closure to C2M again, now. The task stays approved
/// whatever C2M answers; the answer is returned and logged.
/// </summary>
[Authorize(Policy = NwfmPolicies.ReviewTasks)]
public sealed record RetryTaskC2mClosureCommand(Guid TaskId) : IRequest<Result<C2mRetryResultDto>>;

public sealed class RetryTaskC2mClosureCommandValidator : AbstractValidator<RetryTaskC2mClosureCommand>
{
    public RetryTaskC2mClosureCommandValidator() => RuleFor(x => x.TaskId).NotEmpty();
}

public sealed class RetryTaskC2mClosureCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    TaskC2mClosure c2m,
    TimeProvider clock)
    : IRequestHandler<RetryTaskC2mClosureCommand, Result<C2mRetryResultDto>>
{
    public async Task<Result<C2mRetryResultDto>> Handle(RetryTaskC2mClosureCommand request, CancellationToken ct)
    {
        var task = await access.FindForUpdateAsync(request.TaskId, ct);
        if (task is null)
        {
            return Result.Failure<C2mRetryResultDto>(TaskErrors.Task.NotFound);
        }

        if (task.Status != TaskStatuses.Approved || !C2mClosureStatuses.CanRetry(task.C2mStatus))
        {
            return Result.Failure<C2mRetryResultDto>(TaskErrors.C2m.NotRetryable);
        }

        var outcome = await c2m.SendAsync(
            task,
            task.CompletedDate ?? clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromSeconds(c2m.Options.AcknowledgementTimeoutSeconds),
            queueOnTransportFailure: false,
            ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<C2mRetryResultDto>(TaskErrors.Task.ConcurrencyConflict);
        }

        return Result.Success(new C2mRetryResultDto(task.C2mStatus, outcome.Kind.ToString(), outcome.Message, outcome.ResponseCode));
    }
}
