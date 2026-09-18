namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowBindingAssignmentMappingRepository : IWorkflowBindingAssignmentMappingRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowBindingAssignmentMappingRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowBindingAssignmentMapping?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.WorkflowBindingAssignmentMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<List<WorkflowBindingAssignmentMapping>> GetByBindingIdAsync(
        Guid bindingId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.WorkflowBindingAssignmentMappings
            .Where(m => m.WorkflowBindingId == bindingId && m.OrganizationId == organizationId)
            .OrderBy(m => m.AssignmentKey)
            .ToListAsync(cancellationToken);

    public Task<bool> MappingExistsAsync(
        Guid bindingId, string assignmentKey, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.WorkflowBindingAssignmentMappings.AnyAsync(
            m => m.WorkflowBindingId == bindingId &&
                 m.AssignmentKey == assignmentKey &&
                 m.OrganizationId == organizationId,
            cancellationToken);

    public Task<List<string>> GetMappedKeysAsync(
        Guid bindingId, Guid organizationId, CancellationToken cancellationToken = default)
        => _db.WorkflowBindingAssignmentMappings
            .Where(m => m.WorkflowBindingId == bindingId && m.OrganizationId == organizationId && m.IsActive)
            .Select(m => m.AssignmentKey)
            .ToListAsync(cancellationToken);

    public Task<WorkflowBindingAssignmentMapping?> GetByAssignmentKeyAsync(
        Guid bindingId, Guid organizationId, string assignmentKey, CancellationToken cancellationToken = default)
        => _db.WorkflowBindingAssignmentMappings
            .FirstOrDefaultAsync(m =>
                m.WorkflowBindingId == bindingId &&
                m.OrganizationId == organizationId &&
                m.AssignmentKey == assignmentKey &&
                m.IsActive, cancellationToken);

    public async Task AddAsync(WorkflowBindingAssignmentMapping mapping, CancellationToken cancellationToken = default)
        => await _db.WorkflowBindingAssignmentMappings.AddAsync(mapping, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
