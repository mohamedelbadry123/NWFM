namespace Workflow.Application.DTOs;

public sealed record WorkflowDepartmentDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? NameAr,
    string? Code,
    Guid? DefaultAssignmentGroupId,
    bool IsActive,
    int MemberCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowDepartmentMemberDto(
    Guid Id,
    Guid ParticipantId,
    string DisplayName,
    string? DisplayNameAr,
    string Email);
