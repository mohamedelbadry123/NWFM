namespace Workflow.Domain.Enums;

public enum AssignmentStrategy
{
    Manual = 0,
    RoundRobin = 1,
    LeastBusy = 2,
    Random = 3,
    FirstAvailable = 4
}
