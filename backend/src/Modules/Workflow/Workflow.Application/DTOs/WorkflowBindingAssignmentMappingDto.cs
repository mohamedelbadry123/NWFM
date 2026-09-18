namespace Workflow.Application.DTOs;

public sealed record WorkflowBindingAssignmentMappingDto(
    Guid Id,
    Guid OrganizationId,
    Guid WorkflowBindingId,
    string AssignmentKey,
    Guid AssignmentGroupId,
    string? AssignmentGroupName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
