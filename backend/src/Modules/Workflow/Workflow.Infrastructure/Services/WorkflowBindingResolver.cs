namespace Workflow.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

internal sealed class WorkflowBindingResolver : IWorkflowBindingResolver
{
    private readonly WorkflowDbContext _db;

    public WorkflowBindingResolver(WorkflowDbContext db) => _db = db;

    public async Task<Result<WorkflowBinding>> ResolveAsync(
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        CancellationToken cancellationToken = default)
    {
        var binding = await _db.WorkflowBindings
            .FirstOrDefaultAsync(b =>
                b.OrganizationId == organizationId &&
                b.ModuleKey == moduleKey &&
                b.EntityType == entityType &&
                b.TriggerEvent == triggerEvent &&
                b.IsActive,
                cancellationToken);

        if (binding is null)
            return Result.Failure<WorkflowBinding>(WorkflowErrors.Instance.BindingNotActive);

        if (binding.Mode is WorkflowBindingMode.Disabled or WorkflowBindingMode.Paused)
            return Result.Failure<WorkflowBinding>(WorkflowErrors.Instance.BindingDisabled);

        return Result.Success(binding);
    }
}
