namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Operational incident raised when a runtime step fails or requires operator attention.
/// ErrorMessage carries a safe summary — stack traces are never persisted.
/// </summary>
public sealed class WorkflowIncident : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid? ActivityInstanceId { get; private set; }
    public string? ActivityNodeKey { get; private set; }
    public WorkflowIncidentType IncidentType { get; private set; }
    public WorkflowIncidentSeverity Severity { get; private set; }
    public WorkflowIncidentStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTime? IgnoredAt { get; private set; }
    public Guid? IgnoredByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowIncident() { }

    public static WorkflowIncident Create(
        Guid organizationId,
        Guid workflowInstanceId,
        WorkflowIncidentType incidentType,
        WorkflowIncidentSeverity severity,
        string title,
        DateTime createdAt,
        Guid? activityInstanceId = null,
        string? activityNodeKey = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return new WorkflowIncident
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            WorkflowInstanceId = workflowInstanceId,
            ActivityInstanceId = activityInstanceId,
            ActivityNodeKey    = activityNodeKey,
            IncidentType       = incidentType,
            Severity           = severity,
            Status             = WorkflowIncidentStatus.Open,
            Title              = title,
            ErrorCode          = errorCode,
            ErrorMessage       = errorMessage,
            CreatedAt          = createdAt,
        };
    }

    public void MarkInProgress(DateTime updatedAt)
    {
        Status = WorkflowIncidentStatus.InProgress;
        SetUpdated(updatedAt);
    }

    public void Resolve(Guid resolvedByUserId, DateTime resolvedAt, string? notes = null)
    {
        Status            = WorkflowIncidentStatus.Resolved;
        ResolvedByUserId  = resolvedByUserId;
        ResolvedAt        = resolvedAt;
        ResolutionNotes   = notes;
        SetUpdated(resolvedAt);
    }

    public void Ignore(Guid ignoredByUserId, DateTime ignoredAt)
    {
        Status           = WorkflowIncidentStatus.Ignored;
        IgnoredByUserId  = ignoredByUserId;
        IgnoredAt        = ignoredAt;
        SetUpdated(ignoredAt);
    }
}
