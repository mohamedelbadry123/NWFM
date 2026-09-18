namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;
using Workflow.Domain.Entities;

/// <summary>
/// Finds the single active, non-Disabled binding for a given org/module/entity/trigger combination.
/// Used by integration triggers (e.g. Consent shadow) to locate the correct binding before starting.
/// Returns failure if no active binding is found or if the binding is Disabled.
/// </summary>
public interface IWorkflowBindingResolver
{
    Task<Result<WorkflowBinding>> ResolveAsync(
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        CancellationToken cancellationToken = default);
}
