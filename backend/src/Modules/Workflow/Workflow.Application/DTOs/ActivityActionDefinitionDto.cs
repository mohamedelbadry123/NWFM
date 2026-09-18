namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record ActivityActionDefinitionDto(
    Guid Id,
    Guid ActivityDefinitionId,
    string ActionKey,
    ActionExecutionTrigger ExecutionTrigger,
    string? OutcomeKey,
    string? ConditionExpression,
    int Sequence,
    string? InputMappingJson,
    string? OutputMappingJson,
    ActionFailurePolicy FailurePolicy,
    int RetryCount,
    int RetryDelaySeconds,
    int TimeoutSeconds,
    bool IsActive);
