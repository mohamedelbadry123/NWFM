namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Integrations;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private void RoutingAssignments()
    {
        _assignmentResolver.Setup(r => r.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Guid.Empty));
        _candidateFactory.Setup(c => c.CreateCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
    }
    private async Task<(WorkflowBinding Binding, Guid Version)> SeedRoutingAsync(string mode = "All", string policy = "Continue")
    {
        RoutingAssignments();
        var now = DateTime.UtcNow;
        var connection = await Connection(Integrations());
        var graph = WorkflowRoutingTests.Parallel(mode, policy);
        var definition = WorkflowDefinition.Create(_orgId, "routing", "Routing", now); _db.WorkflowDefinitions.Add(definition);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now);
        version.SetWorkspace("{\"designerVersion\":4,\"kind\":\"Main\",\"clusterCode\":\"C\",\"cbuCode\":\"B\"}"); version.Publish(Guid.NewGuid(), now); _db.WorkflowVersions.Add(version);
        var nodes = graph.Nodes.Select(n => ActivityDefinition.Create(version.Id, n.Key,
            n.Key is "one" or "two" or "event16" ? ActivityType.WaitEvent : Enum.Parse<ActivityType>(n.Type), n.Name, now,
            configurationJson: n.Key is "one" or "two" or "event16" ? Config(new EventActivityConfiguration { ConnectionId = connection, EventKey = n.Key })
                : n.Routing is null ? "{}" : JsonSerializer.Serialize(new { routing = n.Routing }, IntegrationJson.Options))).ToDictionary(n => n.NodeKey);
        _db.ActivityDefinitions.AddRange(nodes.Values);
        _db.WorkflowTransitions.AddRange(graph.Connections.Select(e => WorkflowTransition.Create(version.Id, nodes[e.From].Id, nodes[e.To].Id, e.Key, 0, now)));
        var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Routing", "Created", now, mode: WorkflowBindingMode.Active); _db.WorkflowBindings.Add(binding);
        await _db.SaveChangesAsync(); return (binding, version.Id);
    }

    [Theory]
    [InlineData("one,two,event16")]
    [InlineData("one,event16,two")]
    [InlineData("two,one,event16")]
    [InlineData("two,event16,one")]
    [InlineData("event16,one,two")]
    [InlineData("event16,two,one")]
    public async Task Routing_All_DurableArrivalsAgreeWithSimulation(string order)
    {
        var seed = await SeedRoutingAsync(); var now = DateTime.UtcNow;
        var started = await BuildEngine(_db, Integrations()).StartAsync(_orgId, seed.Binding.Id, "test", "routing", now, pinnedWorkflowVersionId: seed.Version);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        foreach (var key in order.Split(','))
        {
            // Recreate the engine for each callback, exercising database-backed recovery.
            var execution = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == key);
            var engine = BuildEngine(_db, Integrations());
            (await engine.CompleteExternalActivityAsync(execution.Id, new Dictionary<string, object?> { ["approved"] = true }, "success", null, now)).IsSuccess.Should().BeTrue();
            (await engine.CompleteExternalActivityAsync(execution.Id, new Dictionary<string, object?> { ["approved"] = false }, "error", "duplicate", now)).IsSuccess.Should().BeTrue();
        }
        (await _db.WorkItems.CountAsync()).Should().Be(1);
        (await _db.RoutingActivations.SingleAsync()).Status.Should().Be("Released");
        (await _db.RoutingResults.CountAsync()).Should().Be(3);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((await _db.RoutingScopes.FindAsync(started.Value.Id))!.ValuesJson)!;
        values["one.approved"].GetBoolean().Should().BeTrue(); values["two.approved"].GetBoolean().Should().BeTrue(); values["event16.approved"].GetBoolean().Should().BeTrue();
    }
    [Theory]
    [InlineData("Continue")]
    [InlineData("Cancel")]
    public async Task Routing_Any_ReleasesOnce_AndKeepsOrCancelsSubscriptions(string policy)
    {
        var seed = await SeedRoutingAsync("Any", policy); var now = DateTime.UtcNow;
        var engine = BuildEngine(_db, Integrations());
        var started = await engine.StartAsync(_orgId, seed.Binding.Id, "test", "any", now, pinnedWorkflowVersionId: seed.Version);
        started.IsSuccess.Should().BeTrue();
        var first = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == "one");
        (await engine.CompleteExternalActivityAsync(first.Id, new Dictionary<string, object?> { ["approved"] = true }, "success", null, now)).IsSuccess.Should().BeTrue();
        (await _db.WorkItems.CountAsync()).Should().Be(1);
        var gate = await _db.RoutingActivations.SingleAsync(); gate.WinnerTokenId.Should().Be(first.ExecutionTokenId);
        if (policy == "Cancel")
        {
            (await _db.EventSubscriptions.CountAsync(s => s.Status == "Cancelled")).Should().Be(2);
            (await _db.WorkflowExecutionTokens.CountAsync(t => t.Status == ExecutionTokenStatus.Cancelled)).Should().Be(2);
        }
        else
        {
            foreach (var key in new[] { "two", "event16" })
            {
                var visit = await _db.ActivityInstances.SingleAsync(a => a.ActivityNodeKey == key);
                await engine.CompleteExternalActivityAsync(visit.Id, new Dictionary<string, object?> { ["approved"] = true }, "success", null, now);
            }
            (await _db.WorkItems.CountAsync()).Should().Be(1);
        }
    }
}
