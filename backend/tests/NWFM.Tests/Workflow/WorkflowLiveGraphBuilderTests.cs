namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Helpers;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowLiveGraphBuilderTests
{
    [Fact]
    public void Build_UsesDesignerPositions_AndMarksCurrentNodeActive()
    {
        var orgId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var start = ActivityDefinition.Create(versionId, "start", ActivityType.Start, "Start", DateTime.UtcNow);
        var review = ActivityDefinition.Create(versionId, "review", ActivityType.UserTask, "Review", DateTime.UtcNow);
        var end = ActivityDefinition.Create(versionId, "end", ActivityType.End, "End", DateTime.UtcNow);
        var t1 = WorkflowTransition.Create(versionId, start.Id, review.Id, "to-review", 0, DateTime.UtcNow, isDefault: true);
        var t2 = WorkflowTransition.Create(versionId, review.Id, end.Id, "to-end", 0, DateTime.UtcNow, isDefault: true);

        var instance = WorkflowInstance.Start(
            orgId, Guid.NewGuid(), versionId, "key", "entity-1", "review", DateTime.UtcNow);

        var startRow = ActivityInstance.Start(orgId, instance.Id, "start", ActivityType.Start, "Start", DateTime.UtcNow.AddMinutes(-2));
        startRow.Complete(DateTime.UtcNow.AddMinutes(-1));
        var reviewRow = ActivityInstance.Start(orgId, instance.Id, "review", ActivityType.UserTask, "Review", DateTime.UtcNow);

        var designerJson = """
            {"nodes":[{"nodeKey":"start","x":40,"y":80},{"nodeKey":"review","x":320,"y":80},{"nodeKey":"end","x":600,"y":80}]}
            """;

        var graph = WorkflowLiveGraphBuilder.Build(
            instance,
            [start, review, end],
            [t1, t2],
            designerJson,
            [startRow, reviewRow]);

        graph.CurrentNodeKey.Should().Be("review");
        graph.InstanceStatus.Should().Be(nameof(WorkflowInstanceStatus.Running));
        graph.Nodes.Should().HaveCount(3);
        graph.Nodes.Single(n => n.NodeKey == "start").Should().BeEquivalentTo(new
        {
            NodeKey = "start",
            RuntimeStatus = "Completed",
            X = 40d,
            Y = 80d
        });
        graph.Nodes.Single(n => n.NodeKey == "review").RuntimeStatus.Should().Be("Active");
        graph.Nodes.Single(n => n.NodeKey == "end").RuntimeStatus.Should().Be("Pending");
        graph.Edges.Should().HaveCount(2);
        graph.Edges.Should().Contain(e => e.FromNodeKey == "start" && e.ToNodeKey == "review");
    }

    [Fact]
    public void Build_WhenInstanceCompleted_LightsCurrentNodeAsCompleted()
    {
        var orgId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var end = ActivityDefinition.Create(versionId, "end", ActivityType.End, "End", DateTime.UtcNow, positionX: 10, positionY: 20);
        var instance = WorkflowInstance.Start(
            orgId, Guid.NewGuid(), versionId, "key", "entity-1", "end", DateTime.UtcNow);
        instance.Complete(DateTime.UtcNow);

        var graph = WorkflowLiveGraphBuilder.Build(instance, [end], [], null, []);

        graph.Nodes.Single().RuntimeStatus.Should().Be("Completed");
        graph.Nodes.Single().X.Should().Be(10);
        graph.Nodes.Single().Y.Should().Be(20);
    }

    [Fact]
    public void Build_WhenActivityFailed_MarksNodeFailed()
    {
        var orgId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var task = ActivityDefinition.Create(versionId, "notify", ActivityType.NotificationTask, "Notify", DateTime.UtcNow);
        var instance = WorkflowInstance.Start(
            orgId, Guid.NewGuid(), versionId, "key", "entity-1", "notify", DateTime.UtcNow);
        var row = ActivityInstance.Start(orgId, instance.Id, "notify", ActivityType.NotificationTask, "Notify", DateTime.UtcNow);
        row.Fail("smtp down", DateTime.UtcNow);

        var graph = WorkflowLiveGraphBuilder.Build(instance, [task], [], null, [row]);

        graph.Nodes.Single().RuntimeStatus.Should().Be("Failed");
    }

    [Fact]
    public void Build_WhenGatewayLeftActive_OnlyCurrentUserTaskIsActive()
    {
        var orgId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var start = ActivityDefinition.Create(versionId, "start", ActivityType.Start, "Start", DateTime.UtcNow);
        var review = ActivityDefinition.Create(versionId, "review", ActivityType.UserTask, "Privacy Review", DateTime.UtcNow);
        var decision = ActivityDefinition.Create(versionId, "decision", ActivityType.ExclusiveGateway, "Decision", DateTime.UtcNow);
        var dpo = ActivityDefinition.Create(versionId, "dpo", ActivityType.UserTask, "DPO Review", DateTime.UtcNow);

        var instance = WorkflowInstance.Start(
            orgId, Guid.NewGuid(), versionId, "key", "entity-1", "dpo", DateTime.UtcNow);

        var startRow = ActivityInstance.Start(orgId, instance.Id, "start", ActivityType.Start, "Start", DateTime.UtcNow.AddMinutes(-3));
        startRow.Complete(DateTime.UtcNow.AddMinutes(-3));
        var reviewRow = ActivityInstance.Start(orgId, instance.Id, "review", ActivityType.UserTask, "Privacy Review", DateTime.UtcNow.AddMinutes(-2));
        reviewRow.Complete(DateTime.UtcNow.AddMinutes(-1));
        var gatewayRow = ActivityInstance.Start(orgId, instance.Id, "decision", ActivityType.ExclusiveGateway, "Decision", DateTime.UtcNow.AddMinutes(-1));
        var dpoRow = ActivityInstance.Start(orgId, instance.Id, "dpo", ActivityType.UserTask, "DPO Review", DateTime.UtcNow);

        var graph = WorkflowLiveGraphBuilder.Build(
            instance,
            [start, review, decision, dpo],
            [],
            null,
            [startRow, reviewRow, gatewayRow, dpoRow]);

        graph.CurrentNodeKey.Should().Be("dpo");
        graph.Nodes.Single(n => n.NodeKey == "start").RuntimeStatus.Should().Be("Completed");
        graph.Nodes.Single(n => n.NodeKey == "review").RuntimeStatus.Should().Be("Completed");
        graph.Nodes.Single(n => n.NodeKey == "decision").RuntimeStatus.Should().Be("Completed");
        graph.Nodes.Single(n => n.NodeKey == "dpo").RuntimeStatus.Should().Be("Active");
    }
}
