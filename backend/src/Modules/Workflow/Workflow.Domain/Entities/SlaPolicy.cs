namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

/// <summary>
/// SLA/escalation policy referenced by timers and user-task due dates.
/// OrganizationId null = global SuperAdmin policy; not ITenantAware.
/// </summary>
public sealed class SlaPolicy : Entity
{
    public Guid? OrganizationId { get; private set; }
    public string PolicyCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int Duration { get; private set; }
    public SlaDurationUnit DurationUnit { get; private set; }
    public Guid BusinessCalendarId { get; private set; }
    public string? ReminderThresholdsJson { get; private set; }
    public string? EscalationThresholdsJson { get; private set; }
    public string? EscalationAssignmentKey { get; private set; }
    public bool IsActive { get; private set; }
    public string? DepartmentCode { get; private set; }
    public string? FieldActivityCode { get; private set; }
    public void BindFieldActivity(string departmentCode, string fieldActivityCode)
    {
        DepartmentCode = departmentCode;
        FieldActivityCode = fieldActivityCode;
        EscalationAssignmentKey = null;
    }
    public byte[] RowVersion { get; private set; } = [];

    private SlaPolicy() { }

    public static SlaPolicy Create(
        string policyCode,
        string name,
        int duration,
        SlaDurationUnit durationUnit,
        Guid businessCalendarId,
        DateTime createdAt,
        Guid? organizationId = null,
        string? nameAr = null,
        string? reminderThresholdsJson = null,
        string? escalationThresholdsJson = null,
        string? escalationAssignmentKey = null)
    {
        return new SlaPolicy
        {
            Id                        = Guid.NewGuid(),
            OrganizationId            = organizationId,
            PolicyCode                = policyCode,
            Name                      = name,
            NameAr                    = nameAr,
            Duration                  = duration,
            DurationUnit              = durationUnit,
            BusinessCalendarId        = businessCalendarId,
            ReminderThresholdsJson    = reminderThresholdsJson,
            EscalationThresholdsJson  = escalationThresholdsJson,
            EscalationAssignmentKey   = escalationAssignmentKey,
            IsActive                  = true,
            CreatedAt                 = createdAt,
        };
    }

    public void Update(
        string name,
        string? nameAr,
        int duration,
        SlaDurationUnit durationUnit,
        Guid businessCalendarId,
        string? reminderThresholdsJson,
        string? escalationThresholdsJson,
        string? escalationAssignmentKey,
        DateTime updatedAt)
    {
        Name                     = name;
        NameAr                   = nameAr;
        Duration                 = duration;
        DurationUnit             = durationUnit;
        BusinessCalendarId       = businessCalendarId;
        ReminderThresholdsJson   = reminderThresholdsJson;
        EscalationThresholdsJson = escalationThresholdsJson;
        EscalationAssignmentKey  = escalationAssignmentKey;
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
}
