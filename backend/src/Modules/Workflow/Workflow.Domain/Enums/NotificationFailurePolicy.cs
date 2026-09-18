namespace Workflow.Domain.Enums;

public enum NotificationFailurePolicy
{
    Continue = 0,
    Retry = 1,
    FailWorkflow = 2,
}
