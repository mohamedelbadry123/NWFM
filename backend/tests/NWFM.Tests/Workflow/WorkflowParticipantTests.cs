namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;

public sealed class WorkflowParticipantTests
{
    [Fact]
    public void Create_WithValidData_SetsPropertiesCorrectly()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var participant = WorkflowParticipant.Create(
            orgId, userId, "John Doe", "john@example.com", now,
            displayNameAr: "جون دو", employeeNumber: "EMP001");

        participant.OrganizationId.Should().Be(orgId);
        participant.UserId.Should().Be(userId);
        participant.DisplayName.Should().Be("John Doe");
        participant.DisplayNameAr.Should().Be("جون دو");
        participant.Email.Should().Be("john@example.com");
        participant.EmployeeNumber.Should().Be("EMP001");
        participant.IsActive.Should().BeTrue();
        participant.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var participant = WorkflowParticipant.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Jane", "jane@test.com", DateTime.UtcNow);

        participant.Deactivate(DateTime.UtcNow);

        participant.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_AfterDeactivation_SetsIsActiveTrue()
    {
        var participant = WorkflowParticipant.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Jane", "jane@test.com", DateTime.UtcNow);

        participant.Deactivate(DateTime.UtcNow);
        participant.Activate(DateTime.UtcNow);

        participant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void WorkflowParticipant_ImplementsITenantAware()
        => typeof(global::Workflow.Domain.Entities.WorkflowParticipant)
            .GetInterfaces()
            .Should().Contain(typeof(global::NWFM.Shared.MultiTenancy.ITenantAware));
}
