namespace Workflow.Domain.Enums;

public enum WorkflowInstanceStatus
{
    Pending   = 0,
    Running   = 1,
    Suspended = 2,
    Completed = 3,
    Cancelled = 4,
    Failed    = 5,
}
