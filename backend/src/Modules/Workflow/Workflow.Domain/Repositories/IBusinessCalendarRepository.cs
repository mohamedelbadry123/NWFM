namespace Workflow.Domain.Repositories;

using Workflow.Domain.Entities;

public interface IBusinessCalendarRepository
{
    Task<BusinessCalendar?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BusinessCalendar?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BusinessCalendar?> GetByCodeAsync(string code, Guid? organizationId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<BusinessCalendar> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task AddAsync(BusinessCalendar calendar, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
