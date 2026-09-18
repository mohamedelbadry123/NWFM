namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowNotificationLogRepository : IWorkflowNotificationLogRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowNotificationLogRepository(WorkflowDbContext db) => _db = db;

    public async Task AddAsync(WorkflowNotificationLog log, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowNotificationLogs.AddAsync(log, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public async Task<(IReadOnlyList<WorkflowNotificationLog> Items, int TotalCount)> GetPagedByOrgAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        Guid? recipientUserId = null,
        CancellationToken cancellationToken = default)
    {
        // Global query filter already scopes by OrganizationId; the explicit where is defense-in-depth.
        var query = _db.WorkflowNotificationLogs
            .Where(n => n.OrganizationId == organizationId);

        if (recipientUserId.HasValue)
        {
            // RecipientsJson contains a JSON array of Guid strings; substring match is adequate here.
            var recipientStr = recipientUserId.Value.ToString();
            query = query.Where(n => n.RecipientsJson.Contains(recipientStr));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
