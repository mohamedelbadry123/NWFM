namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

/// <summary>
/// Working-hours calendar used for SLA and timer due-date calculations.
/// OrganizationId null = global SuperAdmin calendar; not ITenantAware so globals remain queryable.
/// </summary>
public sealed class BusinessCalendar : Entity
{
    public Guid? OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string TimeZone { get; private set; } = "UTC";
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private readonly List<BusinessCalendarPeriod> _periods = [];
    public IReadOnlyList<BusinessCalendarPeriod> Periods => _periods.AsReadOnly();

    private readonly List<BusinessCalendarHoliday> _holidays = [];
    public IReadOnlyList<BusinessCalendarHoliday> Holidays => _holidays.AsReadOnly();

    private BusinessCalendar() { }

    public static BusinessCalendar Create(
        string code,
        string name,
        string timeZone,
        DateTime createdAt,
        Guid? organizationId = null,
        string? nameAr = null)
    {
        return new BusinessCalendar
        {
            Id             = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code           = code,
            Name           = name,
            NameAr         = nameAr,
            TimeZone       = timeZone,
            IsActive       = true,
            CreatedAt      = createdAt,
        };
    }

    public void Update(string name, string? nameAr, string timeZone, DateTime updatedAt)
    {
        Name     = name;
        NameAr   = nameAr;
        TimeZone = timeZone;
        SetUpdated(updatedAt);
    }

    public void Activate(DateTime updatedAt)
    {
        IsActive = true;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime updatedAt)
    {
        IsActive = false;
        SetUpdated(updatedAt);
    }

    public BusinessCalendarPeriod AddPeriod(
        DayOfWeek dayOfWeek,
        TimeSpan startTime,
        TimeSpan endTime,
        DateTime createdAt,
        bool isWorkingTime = true)
    {
        var period = BusinessCalendarPeriod.Create(
            Id, dayOfWeek, startTime, endTime, createdAt, isWorkingTime);
        _periods.Add(period);
        return period;
    }

    public BusinessCalendarHoliday AddHoliday(
        DateOnly holidayDate,
        string name,
        DateTime createdAt,
        string? nameAr = null,
        bool isRecurring = false)
    {
        var holiday = BusinessCalendarHoliday.Create(
            Id, holidayDate, name, createdAt, nameAr, isRecurring);
        _holidays.Add(holiday);
        return holiday;
    }

    public bool RemovePeriod(Guid periodId)
        => _periods.RemoveAll(p => p.Id == periodId) > 0;

    public bool RemoveHoliday(Guid holidayId)
        => _holidays.RemoveAll(h => h.Id == holidayId) > 0;
}
