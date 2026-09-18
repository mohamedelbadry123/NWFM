namespace NWFM.Shared.Integration.Workflow;

[Flags]
public enum WorkflowNotificationChannel
{
    None  = 0,
    Email = 1,
    InApp = 2,
}

public sealed record WorkflowNotificationRequest(
    Guid OrganizationId,
    string TemplateKey,
    WorkflowNotificationChannel Channels,
    IReadOnlyDictionary<string, object?> Variables,
    string CorrelationId,
    IReadOnlyList<Guid> RecipientUserIds);
