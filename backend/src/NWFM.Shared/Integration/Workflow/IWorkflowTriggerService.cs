namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Cross-module contract that allows any module to trigger a workflow for a business entity
/// without depending on Workflow's Application or Infrastructure layers.
/// Implemented in Workflow.Infrastructure; consumed by other modules' Application layers.
/// Shadow mode: starts the instance silently but never modifies the triggering module's data.
/// </summary>
public interface IWorkflowTriggerService
{
    /// <summary>
    /// Idempotently starts a workflow instance if an active Shadow or Active binding exists
    /// for the given module/entity/trigger combination.
    /// Returns silently if no binding is found or if already started (idempotency key match).
    /// </summary>
    Task TriggerAsync(
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        string businessEntityId,
        string idempotencyKey,
        string? correlationId = null,
        IReadOnlyDictionary<string, object?>? payload = null,
        CancellationToken cancellationToken = default);
}
