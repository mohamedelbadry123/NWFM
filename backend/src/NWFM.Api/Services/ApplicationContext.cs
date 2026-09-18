namespace NWFM.Api.Services;

using NWFM.Shared.Abstractions;

public sealed class ApplicationOptions
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = "NWFM";
    public Guid DefaultActorId { get; set; }
    public bool InitializeDatabase { get; set; } = true;
}

public sealed class ApplicationContext(Microsoft.Extensions.Options.IOptions<ApplicationOptions> options)
    : ICurrentTenant, IWorkflowActorContext
{
    public Guid OrganizationId => options.Value.TenantId;
    public Guid ActorId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid DefaultActorId => options.Value.DefaultActorId;
    public void SelectActor(Guid actorId, Guid participantId) => (ActorId, ParticipantId) = (actorId, participantId);
}
