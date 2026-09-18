namespace Workflow.Application.DTOs;

public sealed record WorkflowTransitionDto(
    Guid Id,
    Guid WorkflowVersionId,
    Guid FromActivityDefinitionId,
    Guid ToActivityDefinitionId,
    string TransitionKey,
    string? ConditionExpression,
    bool IsDefault,
    int Priority);
