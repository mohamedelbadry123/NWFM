namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowAssignmentGroupTests
{
    [Fact]
    public void Create_WithValidData_SetsPropertiesCorrectly()
    {
        var orgId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var group = WorkflowAssignmentGroup.Create(
            orgId, "PRIVACY_REVIEW", "Privacy Review", AssignmentStrategy.RoundRobin,
            now, nameAr: "مراجعة الخصوصية");

        group.OrganizationId.Should().Be(orgId);
        group.Code.Should().Be("PRIVACY_REVIEW");
        group.Name.Should().Be("Privacy Review");
        group.NameAr.Should().Be("مراجعة الخصوصية");
        group.AssignmentStrategy.Should().Be(AssignmentStrategy.RoundRobin);
        group.IsActive.Should().BeTrue();
        group.Members.Should().BeEmpty();
        group.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Update_ChangesNameAndStrategy()
    {
        var group = WorkflowAssignmentGroup.Create(
            Guid.NewGuid(), "CODE", "Old Name", AssignmentStrategy.Manual, DateTime.UtcNow);

        group.Update("New Name", "اسم جديد", AssignmentStrategy.LeastBusy, DateTime.UtcNow);

        group.Name.Should().Be("New Name");
        group.NameAr.Should().Be("اسم جديد");
        group.AssignmentStrategy.Should().Be(AssignmentStrategy.LeastBusy);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var group = WorkflowAssignmentGroup.Create(
            Guid.NewGuid(), "CODE", "Group", AssignmentStrategy.Manual, DateTime.UtcNow);

        group.Deactivate(DateTime.UtcNow);

        group.IsActive.Should().BeFalse();
    }

    [Fact]
    public void WorkflowAssignmentGroup_ImplementsITenantAware()
        => typeof(global::Workflow.Domain.Entities.WorkflowAssignmentGroup)
            .GetInterfaces()
            .Should().Contain(typeof(global::NWFM.Shared.MultiTenancy.ITenantAware));
}
