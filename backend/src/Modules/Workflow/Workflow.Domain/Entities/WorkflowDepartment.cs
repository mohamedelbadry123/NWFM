namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowDepartment : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string? Code { get; private set; }
    public Guid? DefaultAssignmentGroupId { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private readonly List<WorkflowDepartmentMember> _members = [];
    public IReadOnlyList<WorkflowDepartmentMember> Members => _members.AsReadOnly();

    private WorkflowDepartment() { }

    public static WorkflowDepartment Create(
        Guid organizationId,
        string name,
        DateTime createdAt,
        string? nameAr = null,
        string? code = null,
        Guid? defaultAssignmentGroupId = null)
    {
        return new WorkflowDepartment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            NameAr = nameAr,
            Code = code,
            DefaultAssignmentGroupId = defaultAssignmentGroupId,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void Update(
        string name,
        string? nameAr,
        string? code,
        Guid? defaultAssignmentGroupId,
        DateTime updatedAt)
    {
        Name = name;
        NameAr = nameAr;
        Code = code;
        DefaultAssignmentGroupId = defaultAssignmentGroupId;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime deactivatedAt)
    {
        IsActive = false;
        SetUpdated(deactivatedAt);
    }
}
