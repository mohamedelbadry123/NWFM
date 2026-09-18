namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowIntegrationOutboxRepository : IWorkflowIntegrationOutboxRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowIntegrationOutboxRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowIntegrationOutbox?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowIntegrationOutbox.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<WorkflowIntegrationOutbox?> GetByMessageIdAsync(
        string messageId, CancellationToken cancellationToken = default)
        => _db.WorkflowIntegrationOutbox.FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowIntegrationOutbox>> GetPendingAsync(
        int take, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowIntegrationOutbox
            .Where(m => m.Status == WorkflowOutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowIntegrationOutbox> Items, int TotalCount)> GetFailedOutboxPagedAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowIntegrationOutbox
            .Where(m => m.OrganizationId == organizationId
                     && m.Status == WorkflowOutboxStatus.Failed);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowIntegrationOutbox message, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowIntegrationOutbox.AddAsync(message, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}



