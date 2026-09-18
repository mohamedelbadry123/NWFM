namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Publishes workflow-driven notifications (email / in-app). Implemented by Notifications module
/// when available; Workflow ships a no-op fallback.
/// </summary>
public interface IWorkflowNotificationPublisher
{
    Task PublishAsync(WorkflowNotificationRequest request, CancellationToken cancellationToken = default);
}
