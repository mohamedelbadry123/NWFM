namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Routing;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Workflow;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData(ActivityType.ServiceTask)]
    [InlineData(ActivityType.ScriptTask)]
    [InlineData(ActivityType.NotificationTask)]
    [InlineData(ActivityType.Timer)]
    public async Task Routing_LocalEventResult_IsAvailableToFollowingCondition(ActivityType type)
    {
        var now = DateTime.UtcNow;
        var definition = WorkflowDefinition.Create(_orgId, "event-result", "Event result", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
        version.SetWorkspace("{\"designerVersion\":4,\"kind\":\"Main\",\"clusterCode\":\"C\",\"cbuCode\":\"B\"}");
        version.Publish(Guid.NewGuid(), now);
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
        var activity = ActivityDefinition.Create(version.Id, "event", type, "Event", now, actionKey: "test.routing",
            configurationJson: type == ActivityType.Timer ? "{\"timerType\":\"Duration\",\"duration\":\"00:00:00\"}" : "{}");
        var condition = ActivityDefinition.Create(version.Id, "condition", ActivityType.ExclusiveGateway, "Event succeeded", now,
            configurationJson: JsonSerializer.Serialize(new { routing = WorkflowRoutingTests.Rule("event", "$outcome", "text", "equal", "success", "done") }, IntegrationJson.Options));
        var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
        var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Event", "Created", now, mode: WorkflowBindingMode.Active);
        _db.AddRange(definition, version, start, activity, condition, end, binding);
        _db.WorkflowTransitions.AddRange(WorkflowTransition.Create(version.Id, start.Id, activity.Id, "entry", 0, now),
            WorkflowTransition.Create(version.Id, activity.Id, condition.Id, "check", 0, now),
            WorkflowTransition.Create(version.Id, condition.Id, end.Id, "done", 0, now));
        var provider = new Mock<IWorkflowActionProvider>();
        provider.Setup(p => p.ExecuteAsync(It.IsAny<WorkflowActionExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkflowActionExecutionResult.Succeeded());
        _actionRegistry.Setup(r => r.Resolve("test.routing")).Returns(provider.Object);
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db);
        var result = await engine.StartAsync(_orgId, binding.Id, "test", "event", now, pinnedWorkflowVersionId: version.Id);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        if (type == ActivityType.Timer)
            (await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now)).IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.RoutingResults.SingleAsync()).Outcome.Should().Be("success");
    }

    [Theory]
    [InlineData("OutcomeKey == 'APPROVE'", false)]
    [InlineData("amount > 10", true)]
    public async Task Routing_UpgradePreservesPriorityAndFlagsUnsupportedExpressions(string expression, bool unsupported)
    {
        var now = DateTime.UtcNow;
        var definition = WorkflowDefinition.Create(_orgId, "upgrade-routing", "Upgrade", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
        version.SetWorkspace("{\"designerVersion\":3,\"kind\":\"Main\"}");
        var task = ActivityDefinition.Create(version.Id, "step", ActivityType.UserTask, "Registration", now,
            configurationJson: "{\"assignmentRoleIds\":[\"11111111-1111-1111-1111-111111111111\"],\"routingOutputs\":[{\"key\":\"answer\",\"type\":\"text\"}]}");
        var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
        var first = WorkflowTransition.Create(version.Id, task.Id, end.Id, "approve", 1, now, conditionExpression: expression);
        var fallback = WorkflowTransition.Create(version.Id, task.Id, end.Id, "otherwise", 2, now, isDefault: true);
        _db.AddRange(definition, version, task, end, first, fallback); await _db.SaveChangesAsync();
        var document = new XElement("Workflow", new XAttribute("workspaceJson", version.WorkspaceJson!),
            new XElement("Activities", version.Activities.Select(a => new XElement("Activity", new XAttribute("nodeKey", a.NodeKey),
                new XAttribute("type", a.ActivityType), new XAttribute("name", a.Name), new XAttribute("configurationJson", a.ConfigurationJson ?? "{}")))),
            new XElement("Transitions", version.Transitions.Select(t => new XElement("Transition", new XAttribute("key", t.TransitionKey), new XAttribute("from", "step"), new XAttribute("to", "end")))));
        version.UpdateXml(document.ToString(), "before", now);
        var converted = RoutingUpgrade.Convert(version, [new("step.$outcome", "step", "$outcome", "text", "Outcome")]);
        converted.Diagnostics.Any(d => d.Code == "ROUTING_UPGRADE_EXPRESSION").Should().Be(unsupported);
        var xml = XDocument.Parse(converted.Xml!);
        var gate = xml.Descendants("Activity").Single(a => a.Attribute("type")?.Value == "ExclusiveGateway");
        var routing = RoutingDefinition.Read(gate.Attribute("configurationJson")!.Value)!;
        routing.MatchMode.Should().Be("First");
        if (!unsupported) routing.Routes.Should().ContainSingle(r => !r.Always);
        xml.Descendants("Activity").Single(a => a.Attribute("nodeKey")?.Value == "step").Attribute("configurationJson")!.Value.Should().Contain("11111111-1111-1111-1111-111111111111");
        version.XmlContent.Should().Be(document.ToString(), "preview must not modify the saved draft");
    }
}
