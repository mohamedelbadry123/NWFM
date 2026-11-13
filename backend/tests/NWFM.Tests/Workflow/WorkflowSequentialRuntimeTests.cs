using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Workflow.Application.Integrations;
using Workflow.Application.Routing;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace NWFM.Tests.Modules.Workflow;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SequentialRouting_LiveUsesSharedTraceAndStopsOnVisitedMissing(bool firstMatches)
    {
        var now = DateTime.UtcNow;
        var definition = WorkflowDefinition.Create(_orgId, "sequential", "Sequential", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
        version.SetWorkspace("{\"designerVersion\":4,\"kind\":\"Main\",\"clusterCode\":\"C\",\"cbuCode\":\"B\"}"); version.Publish(Guid.NewGuid(), now);
        var config = new RoutingConfiguration
        {
            ContractVersion = 2,
            MatchMode = "First",
            Sources = [new("$input.missing", "$input", "missing", "yes_no", "Required input")],
            Routes = [new() { TransitionKey = "first", Always = firstMatches, When = new() { Conditions = [new() { Field = "$input.missing", Operator = "equal", Value = JsonSerializer.SerializeToElement(true) }] } },
                new() { TransitionKey = "second", When = new() { Conditions = [new() { Field = "$input.missing", Operator = "equal", Value = JsonSerializer.SerializeToElement(true) }] } }]
        };
        var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
        var condition = ActivityDefinition.Create(version.Id, "condition", ActivityType.ExclusiveGateway, "Condition", now, configurationJson: JsonSerializer.Serialize(new { routing = config }, IntegrationJson.Options));
        var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
        var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Sequential", "Created", now, mode: WorkflowBindingMode.Active);
        _db.AddRange(definition, version, start, condition, end, binding);
        _db.AddRange(WorkflowTransition.Create(version.Id, start.Id, condition.Id, "entry", 0, now),
            WorkflowTransition.Create(version.Id, condition.Id, end.Id, "first", 0, now), WorkflowTransition.Create(version.Id, condition.Id, end.Id, "second", 1, now));
        await _db.SaveChangesAsync();
        var result = await BuildEngine(_db).StartAsync(_orgId, binding.Id, "entity", "seq", now, pinnedWorkflowVersionId: version.Id);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        var audit = await _db.WorkflowEvents.SingleAsync(e => e.ActivityNodeKey == "condition" && e.EventType == (firstMatches ? WorkflowEventType.ActivityCompleted : WorkflowEventType.ActivityFailed));
        using var json = JsonDocument.Parse(audit.PayloadJson!);
        var rules = json.RootElement.GetProperty("rules");
        rules[0].GetProperty("Status").GetString().Should().Be(firstMatches ? "Matched" : "Checked");
        rules[1].GetProperty("Status").GetString().Should().Be("Skipped");
        result.Value.Status.Should().Be(firstMatches ? WorkflowInstanceStatus.Completed : WorkflowInstanceStatus.Running);
    }
}
