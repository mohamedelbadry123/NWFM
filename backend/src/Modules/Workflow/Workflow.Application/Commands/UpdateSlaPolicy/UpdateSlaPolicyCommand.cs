namespace Workflow.Application.Commands.UpdateSlaPolicy;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record UpdateSlaPolicyCommand(
    Guid Id,
    string Name,
    string? NameAr,
    int Duration,
    SlaDurationUnit DurationUnit,
    Guid BusinessCalendarId,
    string? ReminderThresholdsJson,
    string? EscalationThresholdsJson,
    string? EscalationAssignmentKey,
    bool IsActive) : IRequest<Result<SlaPolicyDto>>;
