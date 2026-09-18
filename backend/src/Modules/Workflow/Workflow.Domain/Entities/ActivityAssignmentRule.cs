namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

public sealed class ActivityAssignmentRule : Entity
{
    public Guid ActivityDefinitionId { get; private set; }
    public AssigneeType AssigneeType { get; private set; }
    /// <summary>
    /// Logical purpose of the assignment (e.g. "PrivacyReview"). Not a tenant group id.
    /// </summary>
    public string? AssignmentPurpose { get; private set; }

    /// <summary>
    /// Group code copied from the selected assignment group (display / SLA escalation).
    /// Runtime prefers <see cref="ReferenceId"/> (the group's Id) when present.
    /// </summary>
    public string? AssignmentKey { get; private set; }

    /// <summary>
    /// For AssigneeType.AssignmentGroup this is the organization's WorkflowAssignmentGroup.Id.
    /// </summary>
    public Guid? ReferenceId { get; private set; }
    public string? Expression { get; private set; }
    public int Priority { get; private set; }
    public bool IsFallback { get; private set; }
    public bool IsActive { get; private set; }

    private ActivityAssignmentRule() { }

    public static ActivityAssignmentRule Create(
        Guid activityDefinitionId,
        AssigneeType assigneeType,
        int priority,
        bool isFallback,
        DateTime createdAt,
        string? assignmentKey = null,
        Guid? referenceId = null,
        string? expression = null,
        string? assignmentPurpose = null)
    {
        return new ActivityAssignmentRule
        {
            Id = Guid.NewGuid(),
            ActivityDefinitionId = activityDefinitionId,
            AssigneeType = assigneeType,
            AssignmentPurpose = assignmentPurpose,
            AssignmentKey = assignmentKey,
            ReferenceId = referenceId,
            Expression = expression,
            Priority = priority,
            IsFallback = isFallback,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void SetAssignmentKey(string? key, DateTime updatedAt)
    {
        AssignmentKey = key;
        SetUpdated(updatedAt);
    }

    public void SetAssignmentPurpose(string? purpose, DateTime updatedAt)
    {
        AssignmentPurpose = purpose;
        SetUpdated(updatedAt);
    }
}
