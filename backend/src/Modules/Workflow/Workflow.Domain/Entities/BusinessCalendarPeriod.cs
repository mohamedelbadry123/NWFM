namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

/// <summary>
/// Weekly working window for a business calendar (local time-of-day in the calendar timezone).
/// </summary>
public sealed class BusinessCalendarPeriod : Entity
{
    public Guid CalendarId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public bool IsWorkingTime { get; private set; }

    public BusinessCalendar Calendar { get; private set; } = null!;

    private BusinessCalendarPeriod() { }

    public static BusinessCalendarPeriod Create(
        Guid calendarId,
        DayOfWeek dayOfWeek,
        TimeSpan startTime,
        TimeSpan endTime,
        DateTime createdAt,
        bool isWorkingTime = true)
    {
        return new BusinessCalendarPeriod
        {
            Id            = Guid.NewGuid(),
            CalendarId    = calendarId,
            DayOfWeek     = dayOfWeek,
            StartTime     = startTime,
            EndTime       = endTime,
            IsWorkingTime = isWorkingTime,
            CreatedAt     = createdAt,
        };
    }

    public void Update(TimeSpan startTime, TimeSpan endTime, bool isWorkingTime, DateTime updatedAt)
    {
        StartTime     = startTime;
        EndTime       = endTime;
        IsWorkingTime = isWorkingTime;
        SetUpdated(updatedAt);
    }
}
