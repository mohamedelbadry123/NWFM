namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class ActivityInstanceRepository : IActivityInstanceRepository
{
    private readonly WorkflowDbContext _db;

    public ActivityInstanceRepository(WorkflowDbContext db) => _db = db;

    public Task<ActivityInstance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.ActivityInstances.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActivityInstance>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.ActivityInstances
            .Where(a => a.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(a => a.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityInstance>> GetByInstanceIdForSuperAdminAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: SuperAdmin live graph colors activity rows across tenants.
        return await _db.ActivityInstances
            
            .Where(a => a.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(a => a.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActivityInstance activityInstance, CancellationToken cancellationToken = default)
    {
        await _db.ActivityInstances.AddAsync(activityInstance, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
