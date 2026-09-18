namespace Workflow.Application.Commands.CreateSlaPolicy;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record CreateSlaPolicyCommand(
    string PolicyCode,
    string Name,
    int Duration,
    SlaDurationUnit DurationUnit,
    Guid BusinessCalendarId,
    string? NameAr = null,
    Guid? OrganizationId = null,
    string? ReminderThresholdsJson = null,
    string? EscalationThresholdsJson = null,
    string? EscalationAssignmentKey = null) : IRequest<Result<SlaPolicyDto>>;
