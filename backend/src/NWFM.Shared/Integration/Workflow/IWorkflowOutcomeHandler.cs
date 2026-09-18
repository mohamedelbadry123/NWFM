namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Module-owned handler that applies an Active-mode workflow terminal outcome
/// to the owning business entity. Shadow-mode outcomes must never invoke this.
/// </summary>
public interface IWorkflowOutcomeHandler
{
    string ModuleKey { get; }

    Task HandleAsync(WorkflowOutcomeMessage message, CancellationToken cancellationToken = default);
}
