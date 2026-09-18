namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowTimerDto(
    Guid Id,
    Guid OrganizationId,
    Guid WorkflowInstanceId,
    Guid ActivityInstanceId,
    WorkflowTimerType TimerType,
    DateTime DueAt,
    string? SignalKey,
    WorkflowTimerStatus Status,
    int AttemptCount,
    DateTime? LastAttemptAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime CreatedAt);

public sealed record WorkflowTimerSummaryDto(
    Guid Id,
    WorkflowTimerType TimerType,
    DateTime DueAt,
    WorkflowTimerStatus Status,
    string? SignalKey);
