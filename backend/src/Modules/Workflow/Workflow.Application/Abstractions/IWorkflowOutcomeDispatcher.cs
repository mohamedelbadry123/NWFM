namespace Workflow.Application.Abstractions;

using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;

/// <summary>
/// Dispatches workflow terminal outcomes via the outbox / outcome publisher.
/// </summary>
public interface IWorkflowOutcomeDispatcher
{
    Task<Result> DispatchAsync(
        WorkflowOutcomeMessage message,
        CancellationToken cancellationToken = default);
}
