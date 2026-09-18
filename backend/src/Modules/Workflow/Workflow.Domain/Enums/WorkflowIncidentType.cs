namespace Workflow.Domain.Enums;

public enum WorkflowIncidentType
{
    ServiceTaskFailed = 0,
    TimerFailed = 1,
    NotificationFailed = 2,
    UnhandledActivity = 3,
    IntegrationFailed = 4,
    SystemError = 5,
}
