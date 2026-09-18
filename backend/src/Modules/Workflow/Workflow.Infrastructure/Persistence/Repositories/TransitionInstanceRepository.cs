namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class TransitionInstanceRepository : ITransitionInstanceRepository
{
    private readonly WorkflowDbContext _db;

    public TransitionInstanceRepository(WorkflowDbContext db) => _db = db;

    public async Task AddAsync(
        TransitionInstance transitionInstance, CancellationToken cancellationToken = default)
    {
        await _db.TransitionInstances.AddAsync(transitionInstance, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TransitionInstance>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.TransitionInstances
            .Where(t => t.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(t => t.TakenAt)
            .ToListAsync(cancellationToken);
    }
}
