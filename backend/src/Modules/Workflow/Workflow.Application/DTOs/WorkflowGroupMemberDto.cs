namespace Workflow.Application.DTOs;

public sealed record WorkflowGroupMemberDto(
    Guid Id,
    Guid ParticipantId,
    string DisplayName,
    string? DisplayNameAr,
    string Email,
    bool CanClaim,
    bool IsPrimary,
    DateTime? ValidFrom,
    DateTime? ValidTo);
