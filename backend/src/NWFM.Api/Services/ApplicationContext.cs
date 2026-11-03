namespace NWFM.Api.Services;

using NWFM.Shared.Abstractions;

/// <summary>The workflow actor of the current request. The tenant comes from the ambient tenant context.</summary>
public sealed class ApplicationContext : IWorkflowActorContext
{
    public Guid ActorId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public void SelectActor(Guid actorId, Guid participantId) => (ActorId, ParticipantId) = (actorId, participantId);
}
