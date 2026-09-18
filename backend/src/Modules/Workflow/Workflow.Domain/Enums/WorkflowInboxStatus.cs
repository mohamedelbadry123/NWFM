namespace Workflow.Domain.Enums;

public enum WorkflowInboxStatus
{
    Pending    = 0,
    Processing = 1,
    Processed  = 2,
    Failed     = 3,
    DeadLetter = 4,
}
