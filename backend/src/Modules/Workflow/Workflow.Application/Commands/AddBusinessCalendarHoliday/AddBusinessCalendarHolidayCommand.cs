namespace Workflow.Application.Commands.AddBusinessCalendarHoliday;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record AddBusinessCalendarHolidayCommand(
    Guid CalendarId,
    DateOnly HolidayDate,
    string Name,
    string? NameAr = null,
    bool IsRecurring = false) : IRequest<Result<BusinessCalendarHolidayDto>>;
