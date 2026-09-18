namespace NWFM.Shared.Abstractions;

/// <summary>Workflow attribution only; this is not an authenticated identity.</summary>
public interface IWorkflowActorContext
{
    Guid ActorId { get; }
    Guid ParticipantId { get; }
    Guid DefaultActorId { get; }
}
