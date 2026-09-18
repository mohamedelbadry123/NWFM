namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

public sealed class WorkflowAssignmentGroup : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public AssignmentStrategy AssignmentStrategy { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private readonly List<WorkflowGroupMember> _members = [];
    public IReadOnlyList<WorkflowGroupMember> Members => _members.AsReadOnly();

    private WorkflowAssignmentGroup() { }

    public static WorkflowAssignmentGroup Create(
        Guid organizationId,
        string code,
        string name,
        AssignmentStrategy assignmentStrategy,
        DateTime createdAt,
        string? nameAr = null)
    {
        return new WorkflowAssignmentGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            NameAr = nameAr,
            AssignmentStrategy = assignmentStrategy,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void Update(
        string name,
        string? nameAr,
        AssignmentStrategy assignmentStrategy,
        DateTime updatedAt,
        string? code = null)
    {
        if (!string.IsNullOrWhiteSpace(code))
            Code = code.Trim().ToUpperInvariant();
        Name = name;
        NameAr = nameAr;
        AssignmentStrategy = assignmentStrategy;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime deactivatedAt)
    {
        IsActive = false;
        SetUpdated(deactivatedAt);
    }
}
