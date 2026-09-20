namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowVersionDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    int VersionNumber,
    WorkflowVersionStatus Status,
    string SchemaVersion,
    WorkflowValidationStatus ValidationStatus,
    string? ChangeSummary,
    Guid CreatedByUserId,
    Guid? PublishedByUserId,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt, string? WorkspaceJson = null, string? PinnedChildVersionsJson = null);

public sealed record WorkflowVersionDetailDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    int VersionNumber,
    WorkflowVersionStatus Status,
    string SchemaVersion,
    string? DesignerJson,
    WorkflowValidationStatus ValidationStatus,
    string? ValidationResultJson,
    string? ChangeSummary,
    Guid CreatedByUserId,
    Guid? PublishedByUserId,
    DateTime? PublishedAt,
    IReadOnlyList<ActivityDefinitionDto> Activities,
    IReadOnlyList<WorkflowTransitionDto> Transitions,
    IReadOnlyList<WorkflowVariableDefinitionDto> Variables,
    DateTime CreatedAt,
    DateTime UpdatedAt, string? WorkspaceJson = null, string? PinnedChildVersionsJson = null);
