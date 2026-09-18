namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;

public sealed class WorkflowDepartmentTests
{
    [Fact]
    public void Create_WithValidData_SetsPropertiesCorrectly()
    {
        var orgId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var dept = WorkflowDepartment.Create(
            orgId, "Legal", now, nameAr: "القانونية",
            code: "LEGAL", defaultAssignmentGroupId: groupId);

        dept.OrganizationId.Should().Be(orgId);
        dept.Name.Should().Be("Legal");
        dept.NameAr.Should().Be("القانونية");
        dept.Code.Should().Be("LEGAL");
        dept.DefaultAssignmentGroupId.Should().Be(groupId);
        dept.IsActive.Should().BeTrue();
        dept.Members.Should().BeEmpty();
    }

    [Fact]
    public void Update_ChangesFields()
    {
        var dept = WorkflowDepartment.Create(Guid.NewGuid(), "Old", DateTime.UtcNow);
        var newGroupId = Guid.NewGuid();

        dept.Update("New", "جديد", "NEW", newGroupId, DateTime.UtcNow);

        dept.Name.Should().Be("New");
        dept.NameAr.Should().Be("جديد");
        dept.Code.Should().Be("NEW");
        dept.DefaultAssignmentGroupId.Should().Be(newGroupId);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var dept = WorkflowDepartment.Create(Guid.NewGuid(), "HR", DateTime.UtcNow);
        dept.Deactivate(DateTime.UtcNow);
        dept.IsActive.Should().BeFalse();
    }

    [Fact]
    public void WorkflowDepartment_ImplementsITenantAware()
        => typeof(global::Workflow.Domain.Entities.WorkflowDepartment)
            .GetInterfaces()
            .Should().Contain(typeof(global::NWFM.Shared.MultiTenancy.ITenantAware));
}
