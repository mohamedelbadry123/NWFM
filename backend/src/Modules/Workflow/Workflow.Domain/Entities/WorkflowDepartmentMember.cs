namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

public sealed class WorkflowDepartmentMember : Entity
{
    public Guid DepartmentId { get; private set; }
    public Guid ParticipantId { get; private set; }

    public WorkflowDepartment Department { get; private set; } = null!;
    public WorkflowParticipant Participant { get; private set; } = null!;

    private WorkflowDepartmentMember() { }

    public static WorkflowDepartmentMember Create(
        Guid departmentId,
        Guid participantId,
        DateTime createdAt)
    {
        return new WorkflowDepartmentMember
        {
            Id = Guid.NewGuid(),
            DepartmentId = departmentId,
            ParticipantId = participantId,
            CreatedAt = createdAt
        };
    }
}
