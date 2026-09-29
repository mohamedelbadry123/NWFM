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
    /// <summary>
    /// Registers a period or holiday just added to a loaded calendar. They carry their own Guid keys, which EF would
    /// otherwise take for existing rows and UPDATE (affecting nothing) instead of INSERT.
    /// </summary>
    void AddDetail(BusinessCalendarPeriod period);
    void AddDetail(BusinessCalendarHoliday holiday);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
