using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkspaceSla(WorkflowDbContext db, ICurrentTenant tenant, IWorkflowReferenceData references) : IWorkspaceSla
{
    public async Task<IReadOnlyList<SlaCalendarOption>> CalendarsAsync(CancellationToken ct) => await db.BusinessCalendars
        .Where(c => c.IsActive).OrderBy(c => c.Name)
        .Select(c => new SlaCalendarOption(c.Id, c.Name, c.TimeZone, c.Periods.Any(p => p.IsWorkingTime && p.EndTime > p.StartTime))).ToListAsync(ct);
    internal static int[] Thresholds(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<int[]>(json) ?? [];
    internal static WorkspaceSlaRule Map(SlaPolicy p) => new(p.Id, p.Name, p.DepartmentCode!, p.FieldActivityCode!,
        p.Duration, p.DurationUnit, p.BusinessCalendarId, Thresholds(p.ReminderThresholdsJson), Thresholds(p.EscalationThresholdsJson), p.IsActive);
    public async Task<IReadOnlyList<WorkspaceSlaRule>> ListAsync(CancellationToken ct) =>
        (await db.SlaPolicies.Where(p => p.OrganizationId == tenant.OrganizationId && p.DepartmentCode != null)
            .OrderBy(p => p.Name).ToListAsync(ct)).Select(Map).ToList();
    public async Task<WorkspaceSlaRule?> ResolveAsync(string departmentCode, string fieldActivityCode, CancellationToken ct)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.OrganizationId == tenant.OrganizationId && p.IsActive
            && p.DepartmentCode == departmentCode && p.FieldActivityCode == fieldActivityCode, ct);
        if (policy is null) return null;
        var calendar = await db.BusinessCalendars.FirstOrDefaultAsync(c => c.Id == policy.BusinessCalendarId && c.IsActive, ct);
        return calendar is null ? null : Map(policy) with { CalendarName = calendar.Name, TimeZone = calendar.TimeZone };
    }
    public async Task<WorkspaceSlaContext> ContextAsync(string departmentCode, string fieldActivityCode, Guid? excludeVersionId, CancellationToken ct)
    {
        var rules = await db.SlaPolicies.Where(p => p.OrganizationId == tenant.OrganizationId
            && p.DepartmentCode == departmentCode && p.FieldActivityCode == fieldActivityCode).OrderBy(p => p.Name).ToListAsync(ct);
        var active = rules.FirstOrDefault(p => p.IsActive);
        WorkspaceSlaRule? rule = null;
        var calendarActive = false;
        if (active is not null)
        {
            var calendar = await db.BusinessCalendars.FirstOrDefaultAsync(c => c.Id == active.BusinessCalendarId, ct);
            calendarActive = calendar?.IsActive == true;
            rule = Map(active) with { CalendarName = calendar?.Name, TimeZone = calendar?.TimeZone };
        }
        return new(rule, calendarActive, rules.Where(p => !p.IsActive).Select(Map).ToList(),
            await UsageAsync(departmentCode, fieldActivityCode, excludeVersionId, ct));
    }
    private async Task<WorkspaceSlaUsage> UsageAsync(string departmentCode, string fieldActivityCode, Guid? excludeVersionId, CancellationToken ct)
    {
        // Text match narrows the scan in SQL; the JSON check below is authoritative.
        var candidates = await (from a in db.ActivityDefinitions
            join v in db.WorkflowVersions on a.WorkflowVersionId equals v.Id
            join d in db.WorkflowDefinitions on v.WorkflowDefinitionId equals d.Id
            where d.OrganizationId == tenant.OrganizationId && v.WorkspaceJson != null && v.Status != WorkflowVersionStatus.Retired
                && (a.ActivityType == ActivityType.UserTask || a.ActivityType == ActivityType.MainActivity)
                && a.ConfigurationJson != null && a.ConfigurationJson.Contains(departmentCode) && a.ConfigurationJson.Contains(fieldActivityCode)
                && (excludeVersionId == null || v.Id != excludeVersionId)
            select new { a.ConfigurationJson, VersionId = v.Id, v.Status, d.Name }).ToListAsync(ct);
        var matches = candidates.Where(c => Uses(c.ConfigurationJson, departmentCode, fieldActivityCode)).ToList();
        var drafts = matches.Where(c => c.Status == WorkflowVersionStatus.Draft).ToList();
        return new(drafts.Count, drafts.Select(c => c.VersionId).Distinct().Count(),
            drafts.Select(c => c.Name).Distinct().Order().Take(5).ToList(),
            matches.Where(c => c.Status == WorkflowVersionStatus.Published).Select(c => c.VersionId).Distinct().Count());
    }
    private static bool Uses(string? json, string departmentCode, string fieldActivityCode)
    {
        try
        {
            var config = WorkspaceDesign.Configuration(json);
            return config["departmentCode"]?.GetValue<string>() == departmentCode && config["fieldActivityCode"]?.GetValue<string>() == fieldActivityCode;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; }
    }
    public async Task<Result<WorkspaceSlaRule>> SaveAsync(Guid? id, WorkspaceSlaInput input, CancellationToken ct)
    {
        Result<WorkspaceSlaRule> Invalid(string message) => Result.Failure<WorkspaceSlaRule>(new Error("Sla.Invalid", message));
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Duration is < 1 or > 87600
            || !Enum.IsDefined(input.DurationUnit)) return Invalid("Enter a name and a positive supported duration.");
        if (!await references.IsValidFieldActivityAsync(input.DepartmentCode, input.FieldActivityCode, ct))
            return Invalid("Select an active department and one of its Field Activity Types.");
        if (input.ReminderMinutes is null || input.OverdueMinutes is null || input.ReminderMinutes.Length > 10 || input.OverdueMinutes.Length > 10
            || input.ReminderMinutes.Any(x => x <= 0 || x > 525600) || input.OverdueMinutes.Any(x => x < 0 || x > 525600))
            return Invalid("Use up to ten reminder offsets before the deadline and ten overdue offsets after it, in minutes.");
        var calendar = await db.BusinessCalendars.Include(c => c.Periods).FirstOrDefaultAsync(c => c.Id == input.CalendarId
            && c.IsActive && (c.OrganizationId == null || c.OrganizationId == tenant.OrganizationId), ct);
        if (calendar is null) return Invalid("Select an active calendar.");
        if (input.DurationUnit is SlaDurationUnit.BusinessDays or SlaDurationUnit.BusinessHours
            && !calendar.Periods.Any(p => p.IsWorkingTime && p.EndTime > p.StartTime)) return Invalid("Business time requires working periods in the calendar.");
        try { TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return Invalid("The calendar timezone is invalid."); }
        if (input.IsActive && await db.SlaPolicies.AnyAsync(p => p.Id != id && p.OrganizationId == tenant.OrganizationId && p.IsActive
            && p.DepartmentCode == input.DepartmentCode && p.FieldActivityCode == input.FieldActivityCode, ct))
            return Invalid("An active SLA rule already exists for this Department and FA Type.");
        var policy = id is null ? null : await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == tenant.OrganizationId && p.DepartmentCode != null, ct);
        if (id is not null && policy is null) return Invalid("SLA rule not found.");
        var reminders = JsonSerializer.Serialize(input.ReminderMinutes.Distinct().OrderDescending());
        var overdue = JsonSerializer.Serialize(input.OverdueMinutes.Distinct().Order());
        var now = DateTime.UtcNow;
        if (policy is null)
        {
            policy = SlaPolicy.Create("FA-" + Guid.NewGuid().ToString("N"), input.Name.Trim(), input.Duration, input.DurationUnit,
                input.CalendarId, now, tenant.OrganizationId, reminderThresholdsJson: reminders, escalationThresholdsJson: overdue);
            db.SlaPolicies.Add(policy);
        }
        else policy.Update(input.Name.Trim(), null, input.Duration, input.DurationUnit, input.CalendarId, reminders, overdue, null, now);
        policy.BindFieldActivity(input.DepartmentCode, input.FieldActivityCode);
        if (input.IsActive) policy.Activate(now); else policy.Deactivate(now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Invalid("The SLA rule conflicts with another saved rule. Refresh and try again."); }
        return Result.Success(Map(policy));
    }
    internal static async Task<PublishedSla> SnapshotAsync(WorkflowDbContext db, SlaPolicy policy, CancellationToken ct)
    {
        var calendar = await db.BusinessCalendars.Include(c => c.Periods).Include(c => c.Holidays)
            .SingleAsync(c => c.Id == policy.BusinessCalendarId && c.IsActive, ct);
        return new(policy.Id, policy.Name, policy.Duration, policy.DurationUnit, calendar.TimeZone,
            calendar.Periods.Where(p => p.IsWorkingTime).Select(p => new SlaCalendarPeriod(p.DayOfWeek, p.StartTime, p.EndTime)).ToArray(),
            calendar.Holidays.Select(h => new SlaCalendarHoliday(h.HolidayDate, h.IsRecurring)).ToArray(),
            Thresholds(policy.ReminderThresholdsJson), Thresholds(policy.EscalationThresholdsJson));
    }
}
