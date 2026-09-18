namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Append-style audit row for workflow notification publish attempts.
/// </summary>
public sealed class WorkflowNotificationLog : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public string Channels { get; private set; } = string.Empty;
    public string RecipientsJson { get; private set; } = "[]";
    public string VariablesJson { get; private set; } = "{}";
    public string? CorrelationId { get; private set; }
    public WorkflowNotificationLogStatus Status { get; private set; }

    private WorkflowNotificationLog() { }

    public static WorkflowNotificationLog Create(
        Guid organizationId,
        string templateKey,
        string channels,
        string recipientsJson,
        string variablesJson,
        DateTime createdAt,
        string? correlationId = null,
        WorkflowNotificationLogStatus status = WorkflowNotificationLogStatus.Logged)
    {
        return new WorkflowNotificationLog
        {
            Id              = Guid.NewGuid(),
            OrganizationId  = organizationId,
            TemplateKey     = templateKey,
            Channels        = channels,
            RecipientsJson  = recipientsJson,
            VariablesJson   = variablesJson,
            CorrelationId   = correlationId,
            Status          = status,
            CreatedAt       = createdAt,
        };
    }
}
