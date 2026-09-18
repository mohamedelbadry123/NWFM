namespace Workflow.Application.Abstractions;

using NWFM.Shared.Results;

/// <summary>
/// Resolves a UserTask assignment to an AssignmentGroupId for a specific organization.
/// Prefers an explicit group id on the activity rule; otherwise looks up the group by code,
/// then falls back to a binding AssignmentKey mapping if one exists.
/// </summary>
public interface IWorkflowAssignmentResolver
{
    Task<Result<Guid>> ResolveGroupAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string assignmentKey,
        CancellationToken cancellationToken = default);

    Task<Result<Guid>> ResolveGroupAsync(
        Guid organizationId,
        Guid workflowBindingId,
        string? assignmentKey,
        Guid? assignmentGroupId,
        CancellationToken cancellationToken = default);
}
