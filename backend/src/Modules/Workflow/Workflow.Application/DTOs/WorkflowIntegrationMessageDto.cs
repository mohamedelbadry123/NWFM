namespace Workflow.Application.DTOs;

public sealed record WorkflowIntegrationInboxMessageDto(
    Guid Id,
    Guid OrganizationId,
    string MessageId,
    string ModuleKey,
    string BusinessEntityType,
    string BusinessEntityId,
    string TriggerEvent,
    string Status,
    int AttemptCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowIntegrationOutboxMessageDto(
    Guid Id,
    Guid OrganizationId,
    string MessageId,
    string ModuleKey,
    string BusinessEntityType,
    string BusinessEntityId,
    string OutcomeKey,
    string Status,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

