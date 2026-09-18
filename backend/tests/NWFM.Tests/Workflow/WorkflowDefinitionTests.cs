namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowDefinitionTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Create_WithValidData_SetsPropertiesCorrectly()
    {
        var now = DateTime.UtcNow;

        var definition = WorkflowDefinition.Create(
            OrgId,
            "LEAVE-APPROVAL", "Leave Approval", now,
            nameAr: "الموافقة على الإجازة",
            description: "Handles leave requests",
            descriptionAr: "يتعامل مع طلبات الإجازة");

        definition.OrganizationId.Should().Be(OrgId);
        definition.DefinitionKey.Should().Be("LEAVE-APPROVAL");
        definition.Name.Should().Be("Leave Approval");
        definition.NameAr.Should().Be("الموافقة على الإجازة");
        definition.IsActive.Should().BeTrue();
        definition.Id.Should().NotBeEmpty();
        definition.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Update_ChangesNameAndDescriptions()
    {
        var definition = WorkflowDefinition.Create(OrgId, "KEY", "Original", DateTime.UtcNow);

        definition.Update("Updated", "محدث", "New desc", "وصف جديد", DateTime.UtcNow);

        definition.Name.Should().Be("Updated");
        definition.NameAr.Should().Be("محدث");
        definition.Description.Should().Be("New desc");
        definition.DescriptionAr.Should().Be("وصف جديد");
    }

    [Fact]
    public void DefinitionKey_IsImmutable_AfterCreate()
    {
        var definition = WorkflowDefinition.Create(OrgId, "IMMUTABLE-KEY", "Name", DateTime.UtcNow);

        definition.DefinitionKey.Should().Be("IMMUTABLE-KEY");

        definition.Update("New Name", null, null, null, DateTime.UtcNow);

        definition.DefinitionKey.Should().Be("IMMUTABLE-KEY");
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var definition = WorkflowDefinition.Create(OrgId, "KEY", "Name", DateTime.UtcNow);

        definition.Deactivate(DateTime.UtcNow);

        definition.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_AfterDeactivation_SetsIsActiveTrue()
    {
        var definition = WorkflowDefinition.Create(OrgId, "KEY", "Name", DateTime.UtcNow);

        definition.Deactivate(DateTime.UtcNow);
        definition.Activate(DateTime.UtcNow);

        definition.IsActive.Should().BeTrue();
    }

    [Fact]
    public void WorkflowDefinition_IsTenantAware()
        => typeof(ITenantAware).IsAssignableFrom(typeof(WorkflowDefinition)).Should().BeTrue();
}
