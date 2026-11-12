namespace Workflow.Application.Abstractions;

public interface IWorkflowDraftLock
{
    Task<T> RunAsync<T>(Guid versionId, Func<Task<T>> action, CancellationToken cancellationToken);
}
