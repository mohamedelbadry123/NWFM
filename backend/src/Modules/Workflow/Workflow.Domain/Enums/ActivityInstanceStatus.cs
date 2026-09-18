namespace Workflow.Domain.Enums;

public enum ActivityInstanceStatus
{
    Pending   = 0,
    Active    = 1,
    Completed = 2,
    Skipped   = 3,
    Failed    = 4,
}
