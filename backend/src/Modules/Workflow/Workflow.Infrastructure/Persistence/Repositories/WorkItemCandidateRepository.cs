namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkItemCandidateRepository : IWorkItemCandidateRepository
{
    private readonly WorkflowDbContext _db;

    public WorkItemCandidateRepository(WorkflowDbContext db) => _db = db;

    public async Task AddRangeAsync(
        IEnumerable<WorkItemCandidate> candidates, CancellationToken cancellationToken = default)
    {
        await _db.WorkItemCandidates.AddRangeAsync(candidates, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkItemCandidate>> GetByWorkItemIdAsync(
        Guid workItemId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkItemCandidates
            .Where(c => c.WorkItemId == workItemId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
