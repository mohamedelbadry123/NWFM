namespace Workflow.Application.DTOs;

public sealed record BusinessCalendarDto(
    Guid Id,
    Guid? OrganizationId,
    string Code,
    string Name,
    string? NameAr,
    string TimeZone,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<BusinessCalendarPeriodDto>? Periods = null,
    IReadOnlyList<BusinessCalendarHolidayDto>? Holidays = null);

public sealed record BusinessCalendarPeriodDto(
    Guid Id,
    Guid CalendarId,
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsWorkingTime);

public sealed record BusinessCalendarHolidayDto(
    Guid Id,
    Guid CalendarId,
    DateOnly HolidayDate,
    string Name,
    string? NameAr,
    bool IsRecurring);
