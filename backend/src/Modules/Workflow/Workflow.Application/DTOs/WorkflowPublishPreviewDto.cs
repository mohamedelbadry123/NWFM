namespace Workflow.Application.DTOs;

public sealed record WorkflowPublishPreviewDto(
    Guid VersionId,
    Guid WorkflowDefinitionId,
    int VersionNumber,
    string Status,
    string ValidationStatus,
    bool CanPublish,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    int ActivityCount,
    int TransitionCount,
    int VariableCount,
    IReadOnlyList<string> RequiredAssignmentKeys,
    Guid? LatestPublishedVersionId,
    int? LatestPublishedVersionNumber,
    string? ChangeSummary);
