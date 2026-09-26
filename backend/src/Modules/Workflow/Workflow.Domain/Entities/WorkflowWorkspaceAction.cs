using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
namespace Workflow.Domain.Entities;

public sealed class WorkflowWorkspaceAction : Entity, ITenantAware
{
    private WorkflowWorkspaceAction() { }
    public Guid OrganizationId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid WorkItemId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid EffectiveActorId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public static WorkflowWorkspaceAction Create(Guid org, Guid request, Guid item, Guid actor, Guid effective, string fingerprint) => new()
    { Id = Guid.NewGuid(), OrganizationId = org, RequestId = request, WorkItemId = item, ActorId = actor, EffectiveActorId = effective, Fingerprint = fingerprint, CreatedAt = DateTime.UtcNow };
}
