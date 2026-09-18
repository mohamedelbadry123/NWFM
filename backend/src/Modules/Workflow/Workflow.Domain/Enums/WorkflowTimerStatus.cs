namespace Workflow.Domain.Enums;

public enum WorkflowTimerStatus
{
    Pending = 0,
    Fired = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4,
}
