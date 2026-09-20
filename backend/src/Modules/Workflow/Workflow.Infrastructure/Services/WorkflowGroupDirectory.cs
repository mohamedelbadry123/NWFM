using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;
namespace Workflow.Infrastructure.Services;

/// <summary>Local projection adapter. Replace this registration when the group-owning module is connected.</summary>
internal sealed class WorkflowGroupDirectory(IWorkflowAssignmentGroupRepository repository) : IWorkflowGroupDirectory
{
    public Task<IReadOnlyList<WorkflowAssignmentGroup>> ListAsync(Guid org, CancellationToken ct) => repository.GetActiveGroupsForOrgAsync(org, ct);
    public Task<bool> IsMemberAsync(Guid org, Guid group, Guid user, CancellationToken ct) => repository.IsUserMemberOfGroupAsync(group, user, org, ct);
}
