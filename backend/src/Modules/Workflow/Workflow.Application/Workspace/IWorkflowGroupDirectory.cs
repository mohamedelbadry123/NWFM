using Workflow.Domain.Entities;
namespace Workflow.Application.Workspace;

public interface IWorkflowGroupDirectory
{
    Task<IReadOnlyList<WorkflowAssignmentGroup>> ListAsync(Guid organizationId, CancellationToken ct);
    Task<bool> IsMemberAsync(Guid organizationId, Guid groupId, Guid userId, CancellationToken ct);
}
