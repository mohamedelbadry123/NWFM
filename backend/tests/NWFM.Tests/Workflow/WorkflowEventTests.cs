namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowEventTests
{
    [Fact]
    public void Append_StoresAllFields()
    {
        var orgId      = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var actorId    = Guid.NewGuid();
        var now        = DateTime.UtcNow;

        var evt = WorkflowEvent.Append(
            orgId, instanceId, WorkflowEventType.InstanceStarted, now,
            "Start", actorId, "{\"key\":\"value\"}");

        evt.Id.Should().NotBeEmpty();
        evt.OrganizationId.Should().Be(orgId);
        evt.WorkflowInstanceId.Should().Be(instanceId);
        evt.EventType.Should().Be(WorkflowEventType.InstanceStarted);
        evt.ActivityNodeKey.Should().Be("Start");
        evt.ActorUserId.Should().Be(actorId);
        evt.PayloadJson.Should().Be("{\"key\":\"value\"}");
        evt.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void Append_NullableFieldsAreOptional()
    {
        var evt = WorkflowEvent.Append(
            Guid.NewGuid(), Guid.NewGuid(),
            WorkflowEventType.TransitionTaken, DateTime.UtcNow);

        evt.ActivityNodeKey.Should().BeNull();
        evt.ActorUserId.Should().BeNull();
        evt.PayloadJson.Should().BeNull();
    }
}
