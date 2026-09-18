namespace NWFM.Shared.Abstractions;

/// <summary>Identifies the authenticated user performing workflow actions.</summary>
public interface IWorkflowActorContext
{
    Guid ActorId { get; }
    Guid ParticipantId { get; }
}
