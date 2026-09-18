namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowTimerRepository : IWorkflowTimerRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowTimerRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowTimer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowTimers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkflowTimer>> GetPendingDueAsync(
        DateTime asOfUtc, int take, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowTimers
            .Where(t => t.Status == WorkflowTimerStatus.Pending && t.DueAt <= asOfUtc)
            .OrderBy(t => t.DueAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTimer>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowTimers
            .Where(t => t.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(t => t.DueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowTimer timer, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowTimers.AddAsync(timer, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
