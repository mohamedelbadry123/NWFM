namespace Workflow.Infrastructure.Services;

using NWFM.Shared.Integration.Workflow;

/// <summary>
/// No-op notification publisher used until the Notifications module provides a real implementation.
/// </summary>
internal sealed class NullWorkflowNotificationPublisher : IWorkflowNotificationPublisher
{
    public Task PublishAsync(WorkflowNotificationRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
