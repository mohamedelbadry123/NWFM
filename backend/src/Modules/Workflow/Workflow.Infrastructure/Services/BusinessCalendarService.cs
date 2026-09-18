namespace Workflow.Infrastructure.Services;

using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

/// <summary>
/// Calculates due timestamps using calendar periods and holidays.
/// Converts between UTC and the calendar IANA timezone for working-hour math.
/// </summary>
internal sealed class BusinessCalendarService : IBusinessCalendarService
{
    private readonly IBusinessCalendarRepository _calendars;

    public BusinessCalendarService(IBusinessCalendarRepository calendars)
        => _calendars = calendars;

    public async Task<DateTime> CalculateDueAtAsync(
        Guid? businessCalendarId,
        DateTime fromUtc,
        int duration,
        SlaDurationUnit durationUnit,
        CancellationToken cancellationToken = default)
    {
        if (duration < 0)
            throw new ArgumentOutOfRangeException(nameof(duration));

        var from = EnsureUtc(fromUtc);

        if (durationUnit is SlaDurationUnit.Minutes or SlaDurationUnit.Hours or SlaDurationUnit.Days
            || businessCalendarId is null)
        {
            return durationUnit switch
            {
                SlaDurationUnit.Minutes => from.AddMinutes(duration),
                SlaDurationUnit.Hours => from.AddHours(duration),
                SlaDurationUnit.Days => from.AddDays(duration),
                SlaDurationUnit.BusinessHours => from.AddHours(duration),
                SlaDurationUnit.BusinessDays => from.AddDays(duration),
                _ => from.AddHours(duration),
            };
        }

        var calendar = await _calendars.GetByIdWithDetailsAsync(businessCalendarId.Value, cancellationToken);
        if (calendar is null || !calendar.IsActive)
        {
            return durationUnit switch
            {
                SlaDurationUnit.BusinessHours => from.AddHours(duration),
                SlaDurationUnit.BusinessDays => from.AddDays(duration),
                _ => from.AddHours(duration),
            };
        }

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.Utc;
        }

        return durationUnit switch
        {
            SlaDurationUnit.BusinessHours => AddBusinessHours(from, duration, calendar, tz),
            SlaDurationUnit.BusinessDays => AddBusinessDays(from, duration, calendar, tz),
            _ => from.AddHours(duration),
        };
    }

    private static DateTime AddBusinessHours(
        DateTime fromUtc,
        int hours,
        BusinessCalendar calendar,
        TimeZoneInfo tz)
    {
        var remaining = TimeSpan.FromHours(hours);
        var cursorLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz);

        // Safety cap: avoid infinite loops on misconfigured calendars.
        for (var guard = 0; guard < 10_000 && remaining > TimeSpan.Zero; guard++)
        {
            if (IsHoliday(calendar, DateOnly.FromDateTime(cursorLocal)))
            {
                cursorLocal = NextDayStart(cursorLocal);
                continue;
            }

            var period = GetWorkingPeriod(calendar, cursorLocal.DayOfWeek);
            if (period is null)
            {
                cursorLocal = NextDayStart(cursorLocal);
                continue;
            }

            var periodStart = cursorLocal.Date + period.StartTime;
            var periodEnd = cursorLocal.Date + period.EndTime;

            if (cursorLocal < periodStart)
                cursorLocal = periodStart;

            if (cursorLocal >= periodEnd)
            {
                cursorLocal = NextDayStart(cursorLocal);
                continue;
            }

            var available = periodEnd - cursorLocal;
            if (remaining <= available)
            {
                cursorLocal += remaining;
                remaining = TimeSpan.Zero;
            }
            else
            {
                remaining -= available;
                cursorLocal = NextDayStart(cursorLocal);
            }
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cursorLocal, DateTimeKind.Unspecified), tz);
    }

    private static DateTime AddBusinessDays(
        DateTime fromUtc,
        int days,
        BusinessCalendar calendar,
        TimeZoneInfo tz)
    {
        var cursorLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz);
        var remaining = days;

        for (var guard = 0; guard < 10_000 && remaining > 0; guard++)
        {
            cursorLocal = cursorLocal.Date.AddDays(1);
            if (IsHoliday(calendar, DateOnly.FromDateTime(cursorLocal)))
                continue;

            if (GetWorkingPeriod(calendar, cursorLocal.DayOfWeek) is null)
                continue;

            remaining--;
        }

        // Preserve original local time-of-day on the resulting business day.
        var originalLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, tz);
        var resultLocal = cursorLocal.Date + originalLocal.TimeOfDay;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(resultLocal, DateTimeKind.Unspecified), tz);
    }

    private static BusinessCalendarPeriod? GetWorkingPeriod(BusinessCalendar calendar, DayOfWeek day)
        => calendar.Periods.FirstOrDefault(p => p.DayOfWeek == day && p.IsWorkingTime && p.EndTime > p.StartTime);

    private static bool IsHoliday(BusinessCalendar calendar, DateOnly date)
        => calendar.Holidays.Any(h =>
            h.IsRecurring
                ? h.HolidayDate.Month == date.Month && h.HolidayDate.Day == date.Day
                : h.HolidayDate == date);

    private static DateTime NextDayStart(DateTime local)
        => local.Date.AddDays(1);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
