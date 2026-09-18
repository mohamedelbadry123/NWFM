namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowIntegrationInboxRepository : IWorkflowIntegrationInboxRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowIntegrationInboxRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowIntegrationInbox?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowIntegrationInbox.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<WorkflowIntegrationInbox?> GetByMessageIdAsync(
        string messageId, CancellationToken cancellationToken = default)
        => _db.WorkflowIntegrationInbox.FirstOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

    public async Task<IReadOnlyList<WorkflowIntegrationInbox>> GetPendingAsync(
        int take, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowIntegrationInbox
            .Where(m => m.Status == WorkflowInboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkflowIntegrationInbox> Items, int TotalCount)> GetDeadLettersPagedAsync(
        Guid organizationId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.WorkflowIntegrationInbox
            .Where(m => m.OrganizationId == organizationId
                     && m.Status == WorkflowInboxStatus.DeadLetter);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowIntegrationInbox message, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowIntegrationInbox.AddAsync(message, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}



