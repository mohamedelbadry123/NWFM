namespace Workflow.Domain.Enums;

public enum WorkflowNotificationLogStatus
{
    Queued = 0,
    Logged = 1,
    /// <summary>In-app (and configured channels) accepted for delivery.</summary>
    Delivered = 2,
    Failed = 3,
}
