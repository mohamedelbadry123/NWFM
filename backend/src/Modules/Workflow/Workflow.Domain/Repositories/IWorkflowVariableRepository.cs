namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowVariableRepository
{
    Task<WorkflowVariable?> GetByNameAsync(Guid workflowInstanceId, string variableName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowVariable>> GetByInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowVariable variable, CancellationToken cancellationToken = default);
}
