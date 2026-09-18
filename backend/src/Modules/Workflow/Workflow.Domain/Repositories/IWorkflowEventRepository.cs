namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowEventRepository
{
    Task<IReadOnlyList<WorkflowEvent>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task AppendAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default);
}
