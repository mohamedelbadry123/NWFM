namespace Workflow.Application.Commands.AddBusinessCalendarPeriod;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record AddBusinessCalendarPeriodCommand(
    Guid CalendarId,
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsWorkingTime = true) : IRequest<Result<BusinessCalendarPeriodDto>>;
