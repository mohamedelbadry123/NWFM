namespace Workflow.Domain.Enums;

/// <summary>
/// Controls how update/modify (and similar) triggers behave when a matching instance may already exist.
/// </summary>
public enum WorkflowExecutionPolicy
{
    /// <summary>Each accepted trigger starts a new instance/request.</summary>
    StartNewInstance = 0,

    /// <summary>Send an external signal to the currently running matching instance.</summary>
    SignalExistingInstance = 1,

    /// <summary>Start only when no Running/Suspended matching instance exists.</summary>
    StartIfNoRunningInstance = 2,

    /// <summary>Start when no running instance exists, or the latest matching instance is terminal.</summary>
    RestartAfterTerminal = 3,
}
