namespace Workflow.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowVariableRepository : IWorkflowVariableRepository
{
    private readonly WorkflowDbContext _db;

    public WorkflowVariableRepository(WorkflowDbContext db) => _db = db;

    public Task<WorkflowVariable?> GetByNameAsync(
        Guid workflowInstanceId, string variableName, CancellationToken cancellationToken = default)
        => _db.WorkflowVariables
            .FirstOrDefaultAsync(v => v.WorkflowInstanceId == workflowInstanceId
                                   && v.VariableName == variableName, cancellationToken);

    public async Task<IReadOnlyList<WorkflowVariable>> GetByInstanceIdAsync(
        Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowVariables
            .Where(v => v.WorkflowInstanceId == workflowInstanceId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WorkflowVariable variable, CancellationToken cancellationToken = default)
    {
        await _db.WorkflowVariables.AddAsync(variable, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
