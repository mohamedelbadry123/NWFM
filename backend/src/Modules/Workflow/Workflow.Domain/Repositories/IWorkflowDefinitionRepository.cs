namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetByKeyAsync(
        Guid organizationId, string definitionKey, CancellationToken cancellationToken = default);
    Task<bool> KeyExistsAsync(
        Guid organizationId, string definitionKey, CancellationToken cancellationToken = default);
    Task<bool> HasVersionsAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<int> GetVersionCountAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
