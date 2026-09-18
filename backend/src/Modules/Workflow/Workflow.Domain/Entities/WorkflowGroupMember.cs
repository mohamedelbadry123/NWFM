namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

public sealed class WorkflowGroupMember : Entity
{
    public Guid AssignmentGroupId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public bool CanClaim { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTime? ValidFrom { get; private set; }
    public DateTime? ValidTo { get; private set; }

    public WorkflowAssignmentGroup AssignmentGroup { get; private set; } = null!;
    public WorkflowParticipant Participant { get; private set; } = null!;

    private WorkflowGroupMember() { }

    public static WorkflowGroupMember Create(
        Guid assignmentGroupId,
        Guid participantId,
        bool canClaim,
        bool isPrimary,
        DateTime createdAt,
        DateTime? validFrom = null,
        DateTime? validTo = null)
    {
        return new WorkflowGroupMember
        {
            Id = Guid.NewGuid(),
            AssignmentGroupId = assignmentGroupId,
            ParticipantId = participantId,
            CanClaim = canClaim,
            IsPrimary = isPrimary,
            ValidFrom = validFrom,
            ValidTo = validTo,
            CreatedAt = createdAt
        };
    }

    public void Update(bool canClaim, bool isPrimary, DateTime? validFrom, DateTime? validTo, DateTime updatedAt)
    {
        CanClaim = canClaim;
        IsPrimary = isPrimary;
        ValidFrom = validFrom;
        ValidTo = validTo;
        SetUpdated(updatedAt);
    }
}
