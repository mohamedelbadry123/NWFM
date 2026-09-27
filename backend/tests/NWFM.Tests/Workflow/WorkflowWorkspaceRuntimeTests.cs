namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Results;
using global::Workflow.Application.Integrations;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Services;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task Publication_RejectsInvalidGeographyDepartmentFieldTypeAndMissingSlaPolicy()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        var references = new Mock<NWFM.Shared.Integration.Workflow.IWorkflowReferenceData>();
        var repository = new global::Workflow.Infrastructure.Persistence.Repositories.WorkflowVersionRepository(_db);
        var version = (await repository.GetByIdWithProjectionAsync(seeded.Version))!;
        var main = version.Activities.Single(a => a.ActivityType == ActivityType.MainActivity);
        var settings = System.Text.Json.Nodes.JsonNode.Parse(main.ConfigurationJson!)!;
        settings["slaPolicyId"] = Guid.NewGuid().ToString();
        _db.Entry(main).Property(a => a.ConfigurationJson).CurrentValue = settings.ToJsonString();
        var publisher = new WorkflowWorkspacePublisher(_db, references.Object, repository);
        var issues = await publisher.ValidateAsync(version, default);
        issues.Should().Contain(i => i.Code == "WORKSPACE_GEOGRAPHY");
        issues.Should().Contain(i => i.Code == "ACTIVITY_FIELD_TYPE");
        issues.Should().Contain(i => i.Code == "ACTIVITY_SLA");
    }

    [Fact]
    public async Task Publication_RejectsRecursiveChildDefinitions()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var repository = new global::Workflow.Infrastructure.Persistence.Repositories.WorkflowVersionRepository(_db);
        var parent = (await repository.GetByIdWithProjectionAsync(seeded.Version))!;
        var parentKey = (await _db.WorkflowDefinitions.FindAsync(parent.WorkflowDefinitionId))!.DefinitionKey;
        _db.ActivityDefinitions.Add(ActivityDefinition.Create(seeded.ChildVersion, "cycle", ActivityType.MainActivity, "Recursive child", now,
            configurationJson: Config(new { definitionKey = parentKey, versionId = parent.Id })));
        await _db.SaveChangesAsync();
        var publisher = new WorkflowWorkspacePublisher(_db, new Mock<NWFM.Shared.Integration.Workflow.IWorkflowReferenceData>().Object, repository);
        (await publisher.ValidateAsync(parent, default)).Should().Contain(i => i.Code == "CHILD_RECURSION");
    }

    [Fact]
    public async Task MainActivity_BadChildOutputMappingCannotBypassFinalApproval()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var main = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seeded.Version && a.ActivityType == ActivityType.MainActivity);
        var settings = System.Text.Json.Nodes.JsonNode.Parse(main.ConfigurationJson!)!;
        settings["outputMappings"] = System.Text.Json.Nodes.JsonNode.Parse("{\"missing\":\"does.not.exist\"}");
        _db.Entry(main).Property(a => a.ConfigurationJson).CurrentValue = settings.ToJsonString();
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db);
        var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "bad-child-map", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now.AddMinutes(1));
        (await engine.ResumeFromCallActivityAsync(root.Id, "activity", now.AddMinutes(1))).IsFailure.Should().BeTrue();
        (await _db.WorkItems.CountAsync()).Should().Be(0);
        (await _db.ActivityInstances.SingleAsync(a => a.WorkflowInstanceId == root.Id && a.ActivityType == ActivityType.MainActivity)).Phase.Should().Be("ChildFailed");
        root.Status.Should().Be(WorkflowInstanceStatus.Running);
    }

    [Theory]
    [InlineData("Http", true)]
    [InlineData("Soap", true)]
    [InlineData("Email", false)]
    [InlineData("Sms", false)]
    public void ActivityEvents_DefaultDeliveryModeDependsOnKind(string kind, bool required)
    {
        var config = IntegrationJson.Read<global::Workflow.Application.Workspace.ActivityEventConfiguration>(Config(new { kind }));
        config.Required.Should().Be(required);
    }

    [Fact]
    public async Task FailedActivity_QueuesOneFailureNotification_WithoutReopeningWorkflow()
    {
        var now = DateTime.UtcNow;
        var integrations = Integrations();
        var connection = await Connection(integrations, "Http", "http://127.0.0.1:5091", "None");
        var configuration = Config(new { timerType = "Duration", duration = "01:00:00", events = new[] {
            new { id = "failure", trigger = "OnFailure", kind = "Http", required = true, configuration = new { connectionId = connection } }
        } });
        var seeded = SeedSingleActivity(ActivityType.Timer, configuration, now);
        var engine = BuildEngine(_db, integrations);
        var instance = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "failure-notification", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var execution = await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.Timer);
        execution.Fail("Activity failed", now); instance.Fail("Activity failed", now); await _db.SaveChangesAsync();
        var events = new WorkflowActivityEvents(_db, integrations);
        var worker = new WorkflowWorkspaceProcessor(_db, engine, events);
        await worker.ProcessAsync(default); await worker.ProcessAsync(default);
        var job = await _db.IntegrationJobs.SingleAsync(); job.Required.Should().BeFalse();
        job.RecordDelivery(Config(new IntegrationResult(true, 200, "{}", null)), 200); await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db, integrations, new WorkflowIntegrationTransport(), engine, events).ProcessJobAsync(job.Id, default);
        job.Status.Should().Be("Completed"); instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
    }

    [Fact]
    public async Task CommentOccurrences_DeduplicateIndependently_AndBackgroundResultsDoNotChangeVariables()
    {
        var now = DateTime.UtcNow; var integrations = Integrations();
        var connection = await Connection(integrations, "Http", "http://127.0.0.1:5091", "None");
        var config = Config(new { events = new[] { new { id="comment",name="Comment notification",trigger="OnComment",kind="Http",required=false,
            configuration=new { connectionId=connection,outputMappings=new { route="body.status" } } } } });
        var seeded = SeedSingleActivity(ActivityType.Timer, "{\"duration\":\"00:00:00\"}", now);
        var engine = BuildEngine(_db, integrations);
        var instance = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "comments", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var execution = await _db.ActivityInstances.SingleAsync(a=>a.ActivityType==ActivityType.Timer);
        var definition = ActivityDefinition.Create(seeded.Version,"comment-config",ActivityType.UserTask,"Comment config",now,configurationJson:config);
        var events = new WorkflowActivityEvents(_db,integrations);
        (await events.QueueAsync(instance,definition,execution,"OnComment","first",default)).Value.Should().BeTrue();
        await events.QueueAsync(instance,definition,execution,"OnComment","first",default);
        await events.QueueAsync(instance,definition,execution,"OnComment","second",default);
        (await _db.IntegrationJobs.CountAsync()).Should().Be(2);
        instance.Complete(now); execution.Complete(now); await _db.SaveChangesAsync();
        var job = await _db.IntegrationJobs.FirstAsync();
        job.RecordDelivery(Config(new IntegrationResult(true,200,"{\"status\":\"wrong-route\"}",null)),200); await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db,integrations,new WorkflowIntegrationTransport(),engine).ProcessJobAsync(job.Id,default);
        job.Status.Should().Be("Completed");
        (await _db.WorkflowVariables.AnyAsync(v=>v.VariableName=="route")).Should().BeFalse();
    }

    [Fact]
    public async Task RequiredEvents_BlockAdvancement_ThenApplyResponseBeforeRouting()
    {
        var now=DateTime.UtcNow;var integrations=Integrations();var connection=await Connection(integrations,"Http","http://127.0.0.1:5091","None");
        var config=Config(new { events=new[] { new {id="required",name="Required API",trigger="OnApprove",kind="Http",required=true,configuration=new{connectionId=connection,outputMappings=new{externalId="body.id"}}} } });
        var seeded=SeedSingleActivity(ActivityType.UserTask,config,now);
        _assignmentResolver.Setup(r=>r.ResolveGroupAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<string?>(),It.IsAny<Guid?>(),It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Guid.NewGuid()));
        _candidateFactory.Setup(f=>f.CreateCandidatesAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<DateTime>(),It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
        var engine=BuildEngine(_db,integrations);var instance=(await engine.StartAsync(_orgId,seeded.Binding.Id,"case","required",now,pinnedWorkflowVersionId:seeded.Version)).Value;
        var task=await _db.WorkItems.SingleAsync();var actor=Guid.NewGuid();task.Claim(actor,now);task.Complete(actor,"APPROVE",now);await _db.SaveChangesAsync();
        (await engine.AdvanceAsync(instance.Id,task.Id,now)).IsSuccess.Should().BeTrue();
        instance.Status.Should().Be(WorkflowInstanceStatus.Running);
        var execution=await _db.ActivityInstances.SingleAsync(a=>a.ActivityType==ActivityType.UserTask);execution.Phase.Should().Be("WaitingForOutcomeEvents");
        var job=await _db.IntegrationJobs.SingleAsync();job.RecordDelivery(Config(new IntegrationResult(true,200,"{\"id\":\"accepted\"}",null)),200);await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db,integrations,new WorkflowIntegrationTransport(),engine).ProcessJobAsync(job.Id,default);
        await engine.ResumeActivityEventsAsync(execution.Id,default);
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkflowVariables.SingleAsync(v=>v.VariableName=="externalId")).ValueJson.Should().Be("\"accepted\"");
        await engine.ResumeActivityEventsAsync(execution.Id,default);
        (await _db.ActivityInstances.CountAsync(a=>a.ActivityType==ActivityType.End)).Should().Be(1);
    }

    [Fact]
    public async Task PublishedParent_StillUsesPinnedChildAfterChildRetirement()
    {
        var now=DateTime.UtcNow;var seeded=SeedWorkspace(now);
        (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!.Retire(now);await _db.SaveChangesAsync();
        var root=await BuildEngine(_db).StartAsync(_orgId,seeded.Binding.Id,"case","retired-child",now,pinnedWorkflowVersionId:seeded.Version);
        root.IsSuccess.Should().BeTrue(root.IsFailure?root.Error.Message:"");
        (await _db.WorkflowInstances.SingleAsync(i=>i.ParentInstanceId==root.Value.Id)).PinnedWorkflowVersionId.Should().Be(seeded.ChildVersion);
    }

    private (WorkflowBinding Binding, Guid Version, Guid ChildVersion) SeedWorkspace(DateTime now)
    {
        var child = WorkflowDefinition.Create(_orgId, "workspace-child", "Field checklist", now);
        var childVersion = WorkflowVersion.CreateDraft(child.Id, 1, Guid.NewGuid(), now);
        childVersion.SetWorkspace("{\"kind\":\"Child\"}"); childVersion.Publish(Guid.NewGuid(), now);
        var start = ActivityDefinition.Create(childVersion.Id, "start", ActivityType.Start, "Start", now);
        var timer = ActivityDefinition.Create(childVersion.Id, "timer", ActivityType.Timer, "Child work", now, configurationJson: "{\"timerType\":\"Duration\",\"duration\":\"00:00:01\"}");
        var end = ActivityDefinition.Create(childVersion.Id, "end", ActivityType.End, "End", now);
        _db.WorkflowDefinitions.Add(child); _db.WorkflowVersions.Add(childVersion); _db.ActivityDefinitions.AddRange(start, timer, end);
        _db.WorkflowTransitions.AddRange(WorkflowTransition.Create(childVersion.Id, start.Id, timer.Id, "child-enter", 1, now), WorkflowTransition.Create(childVersion.Id, timer.Id, end.Id, "child-exit", 1, now));
        var parent = SeedSingleActivity(ActivityType.MainActivity, Config(new { definitionKey = child.DefinitionKey, versionId = childVersion.Id, slaDurationHours = 24, rejectTargetNodeKey = "activity" }), now);
        var version = _db.WorkflowVersions.Find(parent.Version)!;
        version.SetWorkspace("{\"kind\":\"Main\",\"clusterCode\":\"CC\",\"regionCode\":\"R1\",\"cityCode\":\"C1\"}");
        version.PinChildVersions(JsonSerializer.Serialize(new Dictionary<string, Guid> { ["activity"] = childVersion.Id }));
        var group = Guid.NewGuid();
        _assignmentResolver.Setup(r => r.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(group));
        _candidateFactory.Setup(f => f.CreateCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
        _db.SaveChanges(); return (parent.Binding, parent.Version, childVersion.Id);
    }

    [Fact]
    public async Task MainActivity_WaitsForChild_ResumesOnceAfterReload_AndIncludesChildTimeInSla()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var started = await BuildEngine(_db).StartAsync(_orgId, seeded.Binding.Id, "case", "nested", now, pinnedWorkflowVersionId: seeded.Version);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var rootId = started.Value.Id;
        var child = await _db.WorkflowInstances.SingleAsync(i => i.ParentInstanceId == rootId);
        child.GeographyJson.Should().Be(started.Value.GeographyJson);
        child.PinnedWorkflowVersionId.Should().Be(seeded.ChildVersion);
        (await _db.WorkItems.CountAsync()).Should().Be(0);
        var timer = await _db.WorkflowTimers.SingleAsync();
        _db.ChangeTracker.Clear();
        var engine = BuildEngine(_db);
        (await engine.ResumeFromTimerAsync(timer.Id, now.AddHours(1))).IsSuccess.Should().BeTrue();
        (await engine.ResumeFromCallActivityAsync(rootId, "activity", now.AddHours(2))).IsSuccess.Should().BeTrue();
        (await _db.WorkItems.CountAsync()).Should().Be(1);
        var task = await _db.WorkItems.SingleAsync();
        task.DueAt.Should().Be(now.AddHours(24));
        var main = await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.MainActivity);
        main.Phase.Should().Be("AwaitingApproval");
        main.StartedAt.Should().Be(now);
    }

    [Fact]
    public async Task MainActivity_RejectReentersWithFreshChildAndPreservesPreviousAttempt()
    {
        var now = DateTime.UtcNow; var seeded = SeedWorkspace(now); var engine = BuildEngine(_db);
        var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "rework", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var timer = await _db.WorkflowTimers.SingleAsync();
        await engine.ResumeFromTimerAsync(timer.Id, now.AddSeconds(5));
        await engine.ResumeFromCallActivityAsync(root.Id, "activity", now.AddSeconds(5));
        var item = await _db.WorkItems.SingleAsync(); var actor = Guid.NewGuid();
        item.Claim(actor, now.AddSeconds(6)); item.Complete(actor, "REJECT", now.AddSeconds(7), "Recheck isolation"); await _db.SaveChangesAsync();
        (await engine.AdvanceAsync(root.Id, item.Id, now.AddSeconds(7))).IsSuccess.Should().BeTrue();
        (await _db.WorkflowInstances.CountAsync(i => i.ParentInstanceId == root.Id)).Should().Be(2);
        var attempts = await _db.ActivityInstances.Where(a => a.ActivityType == ActivityType.MainActivity).ToListAsync();
        attempts.Should().ContainSingle(a => a.Status == ActivityInstanceStatus.Completed);
        attempts.Should().ContainSingle(a => a.Phase == "WaitingForChild");
        (await _db.WorkItems.CountAsync()).Should().Be(1, "new parent approval must wait for its fresh child");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainActivity_SuspendedOrCancelledAncestorPreventsDescendantTimer(bool cancel)
    {
        var now = DateTime.UtcNow; var seeded = SeedWorkspace(now); var engine = BuildEngine(_db);
        var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "pause", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        if (cancel) root.Cancel(now); else root.Suspend(now);
        await _db.SaveChangesAsync(); var timer = await _db.WorkflowTimers.SingleAsync();
        (await engine.ResumeFromTimerAsync(timer.Id, now.AddMinutes(1))).IsFailure.Should().BeTrue();
        (await _db.WorkItems.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task MainActivity_FailedChildCannotUnlockApproval_RecoveryCanResume()
    {
        var now = DateTime.UtcNow; var seeded = SeedWorkspace(now); var engine = BuildEngine(_db);
        var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "failure", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var child = await _db.WorkflowInstances.SingleAsync(i => i.ParentInstanceId == root.Id);
        child.Fail("Temporary failure", now); await _db.SaveChangesAsync();
        await engine.ResumeFromCallActivityAsync(root.Id, "activity", now);
        (await _db.WorkItems.AnyAsync()).Should().BeFalse();
        (await _db.ActivityInstances.SingleAsync(a => a.ActivityType == ActivityType.MainActivity)).Phase.Should().Be("ChildFailed");
        child.Resume(now); await _db.SaveChangesAsync();
        await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now.AddMinutes(1));
        await engine.ResumeFromCallActivityAsync(root.Id, "activity", now.AddMinutes(1));
        (await _db.WorkItems.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("1.1", "http://schemas.xmlsoap.org/soap/envelope/")]
    [InlineData("1.2", "http://www.w3.org/2003/05/soap-envelope")]
    public void Soap_MapsNamespacesAndDetectsFaults(string version, string envelope)
    {
        var xml = $"<s:Envelope xmlns:s=\"{envelope}\"><s:Body><r:Result xmlns:r=\"urn:result\">accepted</r:Result></s:Body></s:Envelope>";
        var config = new HttpActivityConfiguration { SoapVersion = version, XmlNamespaces = new() { ["r"] = "urn:result" }, XmlOutputMappings = new() { ["result"] = "//soap:Body/r:Result" } };
        SoapMessage.Map(xml, config)["result"].Should().Be("accepted");
        SoapMessage.HasFault(xml).Should().BeFalse();
        SoapMessage.HasFault($"<s:Envelope xmlns:s=\"{envelope}\"><s:Body><s:Fault /></s:Body></s:Envelope>").Should().BeTrue();
    }

    [Fact]
    public void Soap_RejectsExternalEntities_AndEscapesVariableValues()
    {
        var read = () => SoapMessage.Parse("<!DOCTYPE x [<!ENTITY file SYSTEM 'file:///private'>]><x>&file;</x>");
        read.Should().Throw<System.Xml.XmlException>();
        var rendered = SoapMessage.Render("<request>{{value}}</request>", new() { ["value"] = JsonSerializer.SerializeToElement("<injected>&secret") });
        SoapMessage.Parse(rendered).Root!.Value.Should().Be("<injected>&secret");
        SoapMessage.Parse(rendered).Root!.Elements().Should().BeEmpty();
    }
}
