namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowBindingDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string? WorkflowDefinitionName,
    Guid OrganizationId,
    string ModuleKey,
    string EntityType,
    string TriggerEvent,
    string? Description,
    WorkflowBindingMode Mode,
    WorkflowVersionPolicy VersionPolicy,
    WorkflowExecutionPolicy ExecutionPolicy,
    Guid? FixedWorkflowVersionId,
    string? StartEventKey,
    string? StartConditionExpression,
    string? ScreenKey,
    string? InputMappingJson,
    string? OutcomeMappingJson,
    string? ConditionJson,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
