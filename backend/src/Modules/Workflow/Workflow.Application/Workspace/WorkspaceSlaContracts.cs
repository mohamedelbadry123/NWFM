using NWFM.Shared.Results;
using Workflow.Domain.Enums;

namespace Workflow.Application.Workspace;

public sealed record WorkspaceSlaInput(string Name, string DepartmentCode, string FieldActivityCode,
    int Duration, SlaDurationUnit DurationUnit, Guid CalendarId, int[] ReminderMinutes, int[] OverdueMinutes, bool IsActive = true);
public sealed record WorkspaceSlaRule(Guid Id, string Name, string DepartmentCode, string FieldActivityCode,
    int Duration, SlaDurationUnit DurationUnit, Guid CalendarId, int[] ReminderMinutes, int[] OverdueMinutes, bool IsActive,
    string? CalendarName = null, string? TimeZone = null);
public sealed record SlaCalendarPeriod(DayOfWeek Day, TimeSpan Start, TimeSpan End);
public sealed record SlaCalendarHoliday(DateOnly Date, bool Recurring);
public sealed record PublishedSla(Guid PolicyId, string Name, int Duration, SlaDurationUnit DurationUnit,
    string TimeZone, SlaCalendarPeriod[] Periods, SlaCalendarHoliday[] Holidays, int[] ReminderMinutes, int[] OverdueMinutes);

public interface IWorkspaceSla
{
    Task<IReadOnlyList<SlaCalendarOption>> CalendarsAsync(CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSlaRule>> ListAsync(CancellationToken ct);
    Task<WorkspaceSlaRule?> ResolveAsync(string departmentCode, string fieldActivityCode, CancellationToken ct);
    Task<Result<WorkspaceSlaRule>> SaveAsync(Guid? id, WorkspaceSlaInput input, CancellationToken ct);
}
public sealed record SlaCalendarOption(Guid Id, string Name, string TimeZone);
