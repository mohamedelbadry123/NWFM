namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record WorkflowAssignmentGroupDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy,
    bool IsActive,
    int MemberCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowAssignmentGroupDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy,
    bool IsActive,
    IReadOnlyList<WorkflowGroupMemberDto> Members,
    DateTime CreatedAt,
    DateTime UpdatedAt);
