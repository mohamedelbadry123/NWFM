namespace Workflow.Domain.Enums;

public enum ActionFailurePolicy
{
    Continue,
    Retry,
    FailActivity,
    FailWorkflow,
    CreateIncident
}
