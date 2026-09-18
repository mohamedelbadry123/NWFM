namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Cross-module contract that allows the Workflow module to verify user existence
/// within a tenant without directly accessing IdentityDbContext.
/// Implemented in Identity.Infrastructure; consumed by Workflow.Application.
/// </summary>
public interface IWorkflowUserLookup
{
    Task<WorkflowUserInfo?> GetUserAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
