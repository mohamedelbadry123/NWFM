namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

/// <summary>
/// Resolves a symbolic AssignmentKey (from a global WorkflowVersion activity) to a tenant-specific AssignmentGroup.
/// Each organization maps their own groups to the symbolic keys defined in the workflow design.
/// </summary>
public sealed class WorkflowBindingAssignmentMapping : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkflowBindingId { get; private set; }
    public string AssignmentKey { get; private set; } = string.Empty;
    public Guid AssignmentGroupId { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowBindingAssignmentMapping() { }

    public static WorkflowBindingAssignmentMapping Create(
        Guid organizationId,
        Guid workflowBindingId,
        string assignmentKey,
        Guid assignmentGroupId,
        DateTime createdAt)
    {
        return new WorkflowBindingAssignmentMapping
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            WorkflowBindingId = workflowBindingId,
            AssignmentKey = assignmentKey,
            AssignmentGroupId = assignmentGroupId,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void Update(Guid assignmentGroupId, DateTime updatedAt)
    {
        AssignmentGroupId = assignmentGroupId;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime updatedAt)
    {
        IsActive = false;
        SetUpdated(updatedAt);
    }
}
