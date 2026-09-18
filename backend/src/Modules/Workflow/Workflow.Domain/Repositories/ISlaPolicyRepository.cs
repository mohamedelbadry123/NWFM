namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface ISlaPolicyRepository
{
    Task<SlaPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SlaPolicy?> GetByCodeAsync(string policyCode, Guid? organizationId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SlaPolicy> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task AddAsync(SlaPolicy policy, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
