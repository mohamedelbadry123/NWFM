namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowExecutionTokenRepository : IWorkflowExecutionTokenRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowExecutionTokenRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowExecutionToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowExecutionTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<WorkflowExecutionToken?> GetByBranchAsync(
        Guid workflowInstanceId, string branchKey, CancellationToken cancellationToken = default)
        => _db.WorkflowExecutionTokens.FirstOrDefaultAsync(
            t => t.WorkflowInstanceId == workflowInstanceId && t.BranchKey == branchKey,
            cancellationToken);

    public async Task<IReadOnlyList<WorkflowExecutionToken>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowExecutionTokens
            .Where(t => t.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowExecutionToken token, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowExecutionTokens.AddAsync(token, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
