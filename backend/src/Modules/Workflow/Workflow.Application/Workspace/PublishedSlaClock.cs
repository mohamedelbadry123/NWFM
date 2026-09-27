using Workflow.Domain.Enums;

namespace Workflow.Application.Workspace;

public sealed record SlaAlert(string Key, string Trigger, DateTime At);
public static class PublishedSlaClock
{
    public static DateTime Deadline(PublishedSla sla, DateTime start)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        if (sla.DurationUnit == SlaDurationUnit.Minutes) return start.AddMinutes(sla.Duration);
        if (sla.DurationUnit == SlaDurationUnit.Hours) return start.AddHours(sla.Duration);
        if (sla.DurationUnit == SlaDurationUnit.Days) return start.AddDays(sla.Duration);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(sla.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, timezone);
        var originalTime = local.TimeOfDay;
        bool Holiday(DateTime date) => sla.Holidays.Any(h => h.Recurring ? h.Date.Month == date.Month && h.Date.Day == date.Day : h.Date == DateOnly.FromDateTime(date));
        DateTime Utc(DateTime date)
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            while (timezone.IsInvalidTime(date)) date = date.AddMinutes(1);
            return TimeZoneInfo.ConvertTimeToUtc(date, timezone);
        }
        if (sla.DurationUnit == SlaDurationUnit.BusinessDays)
        {
            var remainingDays = sla.Duration;
            for (var guard = 0; guard < 36600 && remainingDays > 0; guard++)
            {
                local = local.Date.AddDays(1);
                if (!Holiday(local) && sla.Periods.Any(p => p.Day == local.DayOfWeek && p.End > p.Start)) remainingDays--;
            }
            if (remainingDays > 0) throw new InvalidOperationException("SLA calendar cannot satisfy the duration.");
            return Utc(local.Date + originalTime);
        }
        var remaining = TimeSpan.FromHours(sla.Duration);
        for (var guard = 0; guard < 36600; guard++)
        {
            if (!Holiday(local)) foreach (var period in sla.Periods.Where(p => p.Day == local.DayOfWeek && p.End > p.Start).OrderBy(p => p.Start))
            {
                var begin = local > local.Date + period.Start ? local : local.Date + period.Start;
                var end = local.Date + period.End;
                if (begin >= end) continue;
                var available = Utc(end) - Utc(begin);
                if (remaining <= available) return Utc(begin) + remaining;
                remaining -= available; local = end;
            }
            local = local.Date.AddDays(1);
        }
        throw new InvalidOperationException("SLA calendar cannot satisfy the duration.");
    }
    public static SlaAlert[] Alerts(PublishedSla sla, DateTime start)
    {
        var due = Deadline(sla, start);
        return sla.ReminderMinutes.Distinct().Select(m => new SlaAlert("reminder:" + m, "OnSlaReminder", due.AddMinutes(-m) < start ? start : due.AddMinutes(-m)))
            .Concat(sla.OverdueMinutes.Append(0).Distinct().Select(m => new SlaAlert("overdue:" + m, "OnSlaBreach", due.AddMinutes(m))))
            .OrderBy(a => a.At).ThenBy(a => a.Key).ToArray();
    }
}
