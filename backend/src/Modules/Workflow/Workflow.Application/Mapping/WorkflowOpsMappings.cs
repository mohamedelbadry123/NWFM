namespace Workflow.Application.Mapping;

using Workflow.Application.DTOs;
using Workflow.Domain.Entities;

internal static class WorkflowOpsMappings
{
    public static BusinessCalendarDto ToDto(BusinessCalendar c, bool includeDetails = false) =>
        new(c.Id, c.OrganizationId, c.Code, c.Name, c.NameAr, c.TimeZone, c.IsActive,
            c.CreatedAt, c.UpdatedAt,
            includeDetails ? c.Periods.Select(ToDto).ToList() : null,
            includeDetails ? c.Holidays.Select(ToDto).ToList() : null);

    public static BusinessCalendarPeriodDto ToDto(BusinessCalendarPeriod p) =>
        new(p.Id, p.CalendarId, p.DayOfWeek, p.StartTime, p.EndTime, p.IsWorkingTime);

    public static BusinessCalendarHolidayDto ToDto(BusinessCalendarHoliday h) =>
        new(h.Id, h.CalendarId, h.HolidayDate, h.Name, h.NameAr, h.IsRecurring);

    public static SlaPolicyDto ToDto(SlaPolicy p) =>
        new(p.Id, p.OrganizationId, p.PolicyCode, p.Name, p.NameAr, p.Duration, p.DurationUnit,
            p.BusinessCalendarId, p.ReminderThresholdsJson, p.EscalationThresholdsJson,
            p.EscalationAssignmentKey, p.IsActive, p.CreatedAt, p.UpdatedAt);

    public static WorkflowIncidentDto ToDto(WorkflowIncident i) =>
        new(i.Id, i.OrganizationId, i.WorkflowInstanceId, i.ActivityInstanceId, i.ActivityNodeKey,
            i.IncidentType, i.Severity, i.Status, i.Title, i.ErrorCode, i.ErrorMessage,
            i.ResolvedAt, i.ResolvedByUserId, i.ResolutionNotes,
            i.IgnoredAt, i.IgnoredByUserId, i.CreatedAt, i.UpdatedAt);

    public static WorkflowIncidentSummaryDto ToSummaryDto(WorkflowIncident i) =>
        new(i.Id, i.IncidentType, i.Severity, i.Status, i.Title, i.ErrorCode, i.CreatedAt);

    public static WorkflowTimerDto ToDto(WorkflowTimer t) =>
        new(t.Id, t.OrganizationId, t.WorkflowInstanceId, t.ActivityInstanceId,
            t.TimerType, t.DueAt, t.SignalKey, t.Status, t.AttemptCount,
            t.LastAttemptAt, t.CompletedAt, t.CancelledAt, t.CreatedAt);

    public static WorkflowTimerSummaryDto ToSummaryDto(WorkflowTimer t) =>
        new(t.Id, t.TimerType, t.DueAt, t.Status, t.SignalKey);
}
