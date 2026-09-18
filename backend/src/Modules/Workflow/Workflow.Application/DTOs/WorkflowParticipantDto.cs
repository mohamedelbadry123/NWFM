namespace Workflow.Application.DTOs;

public sealed record WorkflowParticipantDto(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    string DisplayName,
    string? DisplayNameAr,
    string Email,
    string? EmployeeNumber,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
