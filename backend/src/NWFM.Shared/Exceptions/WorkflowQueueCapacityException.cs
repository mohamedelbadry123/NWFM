namespace NWFM.Shared.Exceptions;

/// <summary>A tenant's persisted workflow backlog cannot accept more active work.</summary>
public sealed class WorkflowQueueCapacityException(string queueName)
    : InvalidOperationException("The workflow queue has reached its configured capacity. Retry after pending work completes.")
{
    public string QueueName { get; } = queueName;
}
