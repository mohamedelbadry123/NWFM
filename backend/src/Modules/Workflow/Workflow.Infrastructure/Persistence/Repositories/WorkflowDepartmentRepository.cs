namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowDepartmentRepository : IWorkflowDepartmentRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowDepartmentRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowDepartment?> GetByIdAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Departments
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId,
                cancellationToken);

    public Task<WorkflowDepartment?> GetByIdWithMembersAsync(
        Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.Departments
            .Include(d => d.Members)
                .ThenInclude(m => m.Participant)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId,
                cancellationToken);

    public async Task<(IReadOnlyList<WorkflowDepartment> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Departments
            .Include(d => d.Members)
            .Where(d => d.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(d => d.Name.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task AddAsync(WorkflowDepartment department, CancellationToken cancellationToken = default)
        => await _db.Departments.AddAsync(department, cancellationToken);

    public async Task AddMemberAsync(WorkflowDepartmentMember member, CancellationToken cancellationToken = default)
        => await _db.DepartmentMembers.AddAsync(member, cancellationToken);

    public Task<WorkflowDepartmentMember?> GetMemberAsync(
        Guid departmentId, Guid participantId, CancellationToken cancellationToken = default)
        => _db.DepartmentMembers
            .FirstOrDefaultAsync(m => m.DepartmentId == departmentId && m.ParticipantId == participantId,
                cancellationToken);

    public Task RemoveMemberAsync(WorkflowDepartmentMember member, CancellationToken cancellationToken = default)
    {
        _db.DepartmentMembers.Remove(member);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
