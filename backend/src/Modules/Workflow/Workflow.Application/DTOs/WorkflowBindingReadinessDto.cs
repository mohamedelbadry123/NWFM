namespace Workflow.Application.DTOs;

public sealed record WorkflowBindingReadinessDto(
    Guid BindingId,
    Guid OrganizationId,
    Guid? ResolvedVersionId,
    bool IsReady,
    IReadOnlyList<string> RequiredAssignmentKeys,
    IReadOnlyList<string> MappedAssignmentKeys,
    IReadOnlyList<string> UnmappedAssignmentKeys,
    string? BlockingReason);
