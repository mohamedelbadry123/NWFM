namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IWorkflowVersionRepository
{
    Task<WorkflowVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowVersion?> GetByIdWithProjectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowVersion?> GetLatestPublishedAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<WorkflowVersion?> GetLatestPublishedWithProjectionAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<int> GetNextVersionNumberAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<bool> HasDraftAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WorkflowVersion> Items, int TotalCount)> GetPagedByDefinitionAsync(
        Guid definitionId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>Returns all distinct non-null AssignmentKeys from activities in the given version.</summary>
    Task<List<string>> GetRequiredAssignmentKeysAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowVersion version, CancellationToken cancellationToken = default);
    Task ReplaceProjectionAsync(
        Guid versionId,
        IEnumerable<ActivityDefinition> activities,
        IEnumerable<WorkflowTransition> transitions,
        IEnumerable<WorkflowVariableDefinition> variables,
        IEnumerable<ActivityAssignmentRule> rules,
        IEnumerable<ActivityOutcomeDefinition> outcomes,
        IEnumerable<ActivityActionDefinition> actions,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
