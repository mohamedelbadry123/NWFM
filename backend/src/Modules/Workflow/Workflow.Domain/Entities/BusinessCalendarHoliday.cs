namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

/// <summary>
/// Holiday or non-working day for a business calendar.
/// IsRecurring=true means the month/day repeats every year.
/// </summary>
public sealed class BusinessCalendarHoliday : Entity
{
    public Guid CalendarId { get; private set; }
    public DateOnly HolidayDate { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public bool IsRecurring { get; private set; }

    public BusinessCalendar Calendar { get; private set; } = null!;

    private BusinessCalendarHoliday() { }

    public static BusinessCalendarHoliday Create(
        Guid calendarId,
        DateOnly holidayDate,
        string name,
        DateTime createdAt,
        string? nameAr = null,
        bool isRecurring = false)
    {
        return new BusinessCalendarHoliday
        {
            Id          = Guid.NewGuid(),
            CalendarId  = calendarId,
            HolidayDate = holidayDate,
            Name        = name,
            NameAr      = nameAr,
            IsRecurring = isRecurring,
            CreatedAt   = createdAt,
        };
    }

    public void Update(string name, string? nameAr, bool isRecurring, DateTime updatedAt)
    {
        Name        = name;
        NameAr      = nameAr;
        IsRecurring = isRecurring;
        SetUpdated(updatedAt);
    }
}
