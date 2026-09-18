namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowEventRepository : IWorkflowEventRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowEventRepository(WorkflowDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowEvent>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AppendAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowEvents.AddAsync(workflowEvent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
