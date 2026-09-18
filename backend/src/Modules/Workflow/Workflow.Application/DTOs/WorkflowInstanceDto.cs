namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid OrganizationId,
    Guid WorkflowBindingId,
    Guid PinnedWorkflowVersionId,
    string IdempotencyKey,
    string BusinessEntityId,
    string? CorrelationId,
    WorkflowInstanceStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime? SuspendedAt,
    string? FailureReason,
    Guid? StartedByUserId,
    string CurrentActivityNodeKey,
    DateTime CreatedAt,
    DateTime UpdatedAt);
