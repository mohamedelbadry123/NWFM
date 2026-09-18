namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Publishes a workflow terminal outcome for consumption by the owning business module.
/// </summary>
public interface IWorkflowOutcomePublisher
{
    Task PublishAsync(WorkflowOutcomeMessage message, CancellationToken cancellationToken = default);
}
