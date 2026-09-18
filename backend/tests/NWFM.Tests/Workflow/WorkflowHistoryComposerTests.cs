namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Helpers;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowHistoryComposerTests
{
    [Fact]
    public void Compose_CompletedWorkItem_UsesDesignerStepName_Actor_Comment_AndAction()
    {
        var orgId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var definition = ActivityDefinition.Create(
            versionId, "privacy", ActivityType.UserTask, "Privacy Review", now, "مراجعة الخصوصية");
        var activity = ActivityInstance.Start(
            orgId, instanceId, "privacy", ActivityType.UserTask, "User Task", now.AddMinutes(-5));
        var item = WorkItem.Create(orgId, instanceId, activity.Id, Guid.NewGuid(), now.AddMinutes(-5));
        item.Claim(userId, now.AddMinutes(-4));
        item.Complete(userId, "Approve", now, "Looks good");

        var evt = WorkflowEvent.Append(
            orgId, instanceId, WorkflowEventType.WorkItemCompleted, now,
            "privacy", userId, """{"comment":"Looks good","outcome":"Approve"}""");

        var actors = new Dictionary<Guid, WorkflowActorName>
        {
            [userId] = new WorkflowActorName("Sara Ahmed", "سارة أحمد")
        };

        var row = WorkflowHistoryComposer.Compose([evt], [activity], [definition], [item], actors)
            .Should().ContainSingle().Subject;

        row.ActivityNameEn.Should().Be("Privacy Review");
        row.ActivityNameAr.Should().Be("مراجعة الخصوصية");
        row.ActorName.Should().Be("Sara Ahmed");
        row.Comment.Should().Be("Looks good");
        row.ActionTaken.Should().Be("Approve");
        row.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void RelabelActivities_ReplacesGenericUserTaskNameWithDesignerName()
    {
        var orgId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var activity = ActivityInstance.Start(
            orgId, instanceId, "privacy", ActivityType.UserTask, "User Task", DateTime.UtcNow);

        var events = WorkflowHistoryComposer.Compose(
            [WorkflowEvent.Append(
                orgId, instanceId, WorkflowEventType.WorkItemCompleted, DateTime.UtcNow, "privacy")],
            [activity],
            [ActivityDefinition.Create(
                Guid.NewGuid(), "privacy", ActivityType.UserTask, "DPO Review", DateTime.UtcNow)],
            [],
            new Dictionary<Guid, WorkflowActorName>());

        var dto = WorkflowHistoryComposer.RelabelActivities([activity], events)
            .Should().ContainSingle().Subject;

        dto.Name.Should().Be("DPO Review");
    }
}
