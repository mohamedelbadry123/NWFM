namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task TriggerNode_QueuesOncePerComment_AndDoesNotEnterTheSequence()
    {
        var now = DateTime.UtcNow;
        var integrations = Integrations();
        var connection = await Connection(integrations, "Http", "http://127.0.0.1:5091", "None");
        var seeded = SeedSingleActivity(ActivityType.Timer, "{\"duration\":\"01:00:00\"}", now);
        var engine = BuildEngine(_db, integrations);
        var instance = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "visible", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var execution = await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.Timer);
        var source = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seeded.Version && a.ActivityType == ActivityType.Timer);
        _db.ActivityDefinitions.Add(ActivityDefinition.Create(seeded.Version, "comment-api", ActivityType.ServiceTask, "Comment API", now, actionKey: "http.request",
            configurationJson: Config(new { connectionId = connection, required = true, triggerBinding = new { sourceNodeKey = source.NodeKey, trigger = "OnComment" } })));
        await _db.SaveChangesAsync();
        var events = new WorkflowActivityEvents(_db, integrations);
        (await events.QueueAsync(instance, source, execution, "OnComment", "one", default)).Value.Should().BeFalse();
        await events.QueueAsync(instance, source, execution, "OnComment", "one", default);
        await events.QueueAsync(instance, source, execution, "OnComment", "two", default);
        var jobs = await _db.IntegrationJobs.ToListAsync();
        jobs.Should().HaveCount(2).And.OnlyContain(j => j.Required && j.EventNodeKey == "comment-api" && j.ActivityInstanceId == execution.Id);
        (await _db.ActivityInstances.AnyAsync(a => a.ActivityNodeKey == "comment-api")).Should().BeFalse();
        instance.CurrentActivityNodeKey.Should().Be(source.NodeKey);
    }

    [Fact]
    public async Task SlaAlerts_ResumeWithoutDuplicates_AndDoNotReassignOrAdvance()
    {
        var now = DateTime.UtcNow.AddHours(-2);
        var integrations = Integrations();
        var connection = await Connection(integrations, "Http", "http://127.0.0.1:5091", "None");
        var sla = new PublishedSla(Guid.NewGuid(), "Demo", 1, SlaDurationUnit.Hours, "UTC", [], [], [15], [0, 5]);
        var seeded = SeedSingleActivity(ActivityType.Timer, Config(new { duration = "10:00:00", publishedSla = sla }), now);
        var engine = BuildEngine(_db, integrations);
        var instance = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "alerts", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var source = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seeded.Version && a.ActivityType == ActivityType.Timer);
        foreach (var trigger in new[] { "OnSlaReminder", "OnSlaBreach" })
            _db.ActivityDefinitions.Add(ActivityDefinition.Create(seeded.Version, trigger, ActivityType.ServiceTask, trigger, now, actionKey: "http.request",
                configurationJson: Config(new { connectionId = connection, required = false, triggerBinding = new { sourceNodeKey = source.NodeKey, trigger } })));
        await _db.SaveChangesAsync();
        var worker = new WorkflowWorkspaceProcessor(_db, engine, new WorkflowActivityEvents(_db, integrations));
        await worker.ProcessAsync(default); await worker.ProcessAsync(default);
        (await _db.IntegrationJobs.CountAsync()).Should().Be(3);
        (await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.Timer)).SlaAlertCount.Should().Be(3);
        instance.Status.Should().Be(WorkflowInstanceStatus.Running);
    }
}
