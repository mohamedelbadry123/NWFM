namespace Workflow.Application.DTOs;

using Workflow.Domain.Enums;

public sealed record SlaPolicyDto(
    Guid Id,
    Guid? OrganizationId,
    string PolicyCode,
    string Name,
    string? NameAr,
    int Duration,
    SlaDurationUnit DurationUnit,
    Guid BusinessCalendarId,
    string? ReminderThresholdsJson,
    string? EscalationThresholdsJson,
    string? EscalationAssignmentKey,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
