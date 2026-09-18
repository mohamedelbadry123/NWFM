namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record ActivityInstanceDto(
    Guid Id,
    Guid WorkflowInstanceId,
    string ActivityNodeKey,
    ActivityType ActivityType,
    string Name,
    ActivityInstanceStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    string? FailureReason);
