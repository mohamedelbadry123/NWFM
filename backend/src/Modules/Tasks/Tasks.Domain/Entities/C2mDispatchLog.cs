using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// One attempt to close a task's field activity in C2M. Table: <c>Task.C2mDispatchLogs</c>.
/// </summary>
/// <remarks>
/// One row per attempt rather than one per task: when a close is retried, what operations need to see
/// is how many times and against what response, and an overwritten row cannot say. A row is written
/// even when nothing is sent (the integration is off), so a task that never reached C2M still says why.
/// </remarks>
public sealed class C2mDispatchLog : Entity
{
    public const int FaIdMaxLength = 50;
    public const int ResponseCodeMaxLength = 50;
    public const int ErrorMaxLength = 2000;

    private C2mDispatchLog()
    {
    }

    public Guid TaskId { get; private set; }

    /// <summary>The field activity closed — also C2M's <c>transactionId</c>.</summary>
    public string FaId { get; private set; } = default!;

    /// <summary>What C2M was asked to record — <c>C</c> or <c>X</c>.</summary>
    public string OpStatus { get; private set; } = default!;

    /// <summary>1 for the first try; each retry for the same task adds one.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>See <see cref="C2mDispatchStatuses"/>.</summary>
    public string Status { get; private set; } = default!;

    public string RequestJson { get; private set; } = default!;
    public string? ResponseJson { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public static C2mDispatchLog Start(Guid taskId, string faId, string opStatus, int attemptNumber, string requestJson, DateTime utcNow)
    {
        if (taskId == Guid.Empty)
        {
            throw new DomainException("A dispatch log must belong to a task.");
        }

        if (string.IsNullOrWhiteSpace(faId))
        {
            throw new DomainException("A dispatch log must carry the FA id it closes.");
        }

        if (!C2mOperationStatuses.IsDefined(opStatus))
        {
            throw new DomainException($"Unknown C2M operation status '{opStatus}'.");
        }

        if (attemptNumber <= 0)
        {
            throw new DomainException("Attempt numbers start at 1.");
        }

        return new C2mDispatchLog
        {
            TaskId = taskId,
            FaId = faId.Trim(),
            OpStatus = opStatus,
            AttemptNumber = attemptNumber,
            RequestJson = requestJson,
            Status = C2mDispatchStatuses.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void Succeed(string? responseJson, string? responseCode, DateTime utcNow)
    {
        Status = C2mDispatchStatuses.Succeeded;
        ResponseJson = responseJson;
        ResponseCode = Clip(responseCode, ResponseCodeMaxLength);
        ErrorMessage = null;
        Finish(utcNow);
    }

    public void Fail(string? responseJson, string? responseCode, string errorMessage, DateTime utcNow)
    {
        Status = C2mDispatchStatuses.Failed;
        ResponseJson = responseJson;
        ResponseCode = Clip(responseCode, ResponseCodeMaxLength);
        ErrorMessage = Clip(errorMessage, ErrorMaxLength);
        Finish(utcNow);
    }

    public void Skip(string reason, DateTime utcNow)
    {
        Status = C2mDispatchStatuses.Skipped;
        ErrorMessage = Clip(reason, ErrorMaxLength);
        Finish(utcNow);
    }

    private void Finish(DateTime utcNow)
    {
        CompletedAt = utcNow;
        SetUpdated(utcNow);
    }

    private static string? Clip(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
