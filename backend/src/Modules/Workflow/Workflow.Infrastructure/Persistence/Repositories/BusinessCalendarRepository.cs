namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class BusinessCalendarRepository : IBusinessCalendarRepository
{
    private readonly WorkflowDbContext _db;

    public BusinessCalendarRepository(WorkflowDbContext db) => _db = db;

    public Task<BusinessCalendar?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.BusinessCalendars.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<BusinessCalendar?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.BusinessCalendars
            .Include(c => c.Periods)
            .Include(c => c.Holidays)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<BusinessCalendar?> GetByCodeAsync(
        string code, Guid? organizationId, CancellationToken cancellationToken = default)
        => _db.BusinessCalendars.FirstOrDefaultAsync(
            c => c.Code == code && c.OrganizationId == organizationId && c.IsActive,
            cancellationToken);

    public async Task<(IReadOnlyList<BusinessCalendar> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var query = _db.BusinessCalendars.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(c =>
                c.Code.Contains(term) || c.Name.Contains(term) ||
                (c.NameAr != null && c.NameAr.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(BusinessCalendar calendar, CancellationToken cancellationToken = default)
    {
        await _db.BusinessCalendars.AddAsync(calendar, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
