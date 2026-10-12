namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task ConditionalRoutes_CanSkipReturnRepeatAndReenterEarlierActivity_WithoutUsingFormOrder()
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var form = Guid.NewGuid();
        var rules = new List<FormRoutingRule>();
        foreach (var target in new[] { "one", "two", "three", "other", "activity" })
            rules.Add(new()
            {
                Id = target,
                Name = target,
                When = new() { Conditions = [new() { Field = "route", Operator = "equal", Value = JsonSerializer.SerializeToElement(target) }] },
                Destination = new() { Kind = target is "other" or "activity" ? "Activity" : "Form", Target = target }
            });
        var steps = new[] { "one", "two", "three" }.Select(id => new ActivityFormStep
        {
            Id = id,
            FormId = form,
            VersionNo = 1,
            Rules = rules,
            Otherwise = new() { Kind = "Activity", Target = "end" }
        }).ToList();
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new { formSteps = steps }), now);
        var other = ActivityDefinition.Create(seeded.Version, "other", ActivityType.UserTask, "Second activity", now,
            configurationJson: Config(new { formSteps = steps }));
        _db.ActivityDefinitions.Add(other);
        var forward = await _db.WorkflowTransitions.SingleAsync(t => t.WorkflowVersionId == seeded.Version && t.TransitionKey == "exit");
        _db.WorkflowTransitions.Remove(forward);
        _db.WorkflowTransitions.AddRange(
            WorkflowTransition.Create(seeded.Version, forward.FromActivityDefinitionId, other.Id, "to-other", 0, now, true),
            WorkflowTransition.Create(seeded.Version, other.Id, forward.ToActivityDefinitionId, "other-end", 0, now, true));
        await _db.SaveChangesAsync();

        var assignment = Guid.NewGuid();
        _assignmentResolver.Setup(a => a.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(assignment));
        _candidateFactory.Setup(c => c.CreateCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
        var versions = new WorkflowVersionRepository(_db);
        var version = (await versions.GetByIdWithProjectionAsync(seeded.Version))!; version.SetWorkspace(null);
        var engine = BuildEngine(_db);
        var started = await engine.StartAsync(_orgId, seeded.Binding.Id, "dynamic-forms", "dynamic-forms", now, pinnedWorkflowVersionId: version.Id);
        started.IsSuccess.Should().BeTrue();
        var instance = started.Value;
        var item = await _db.WorkItems.SingleAsync(w => w.WorkflowInstanceId == instance.Id); item.Claim(actor, now);
        var firstItemId = item.Id;
        var canonicalRoute = "";
        var forms = new Mock<IFormGateway>();
        forms.Setup(f => f.GetVersionSchemaAsync(form, 1, default)).ReturnsAsync("{}");
        forms.Setup(f => f.GetFieldsAsync(form, 1, default)).ReturnsAsync([new FormFieldInfo("route", "text", "Route", "", null, [])]);
        forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).ReturnsAsync(Result.Success(new FormSubmitReceipt(Guid.NewGuid(), 1, false)));
        forms.Setup(f => f.GetLatestByContextAsync(form, "WorkItem", It.IsAny<string>(), default)).ReturnsAsync(() =>
            new FormSubmissionRecord(Guid.NewGuid(), form, 1, actor.ToString(), "Actor", null, new Dictionary<string, object?> { ["route"] = canonicalRoute }));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine,
            new WorkflowEventAppender(new WorkflowEventRepository(_db)), Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(_db), versions,
            new ActivityInstanceRepository(_db), Mock.Of<IWorkflowAssignmentGroupRepository>(), Mock.Of<IWorkflowDepartmentRepository>(),
            Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), forms.Object);
        async Task Submit(string answer)
        {
            canonicalRoute = answer;
            var visit = ActivityFormProgress.Read(item.FormProgressJson, steps, item.Id).VisitId;
            var result = await handler.Handle(new(item.Id, actor, _orgId, "APPROVE", FormVisitId: visit), default);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        }
        // Every transition is explicit and driven by the saved answer, never Next.
        foreach (var target in new[] { "two", "one", "three", "three", "two" })
        {
            await Submit(target);
            var progress = ActivityFormProgress.Read(item.FormProgressJson, steps, item.Id);
            progress.ActiveStepId.Should().Be(target);
            progress.Visits.Last().RuleId.Should().Be(target);
            item.Status.Should().Be(WorkItemStatus.Claimed);
            item.Id.Should().Be(firstItemId);
        }
        await Submit("other"); // Leave from Form 2, without visiting the final form again.
        item.Status.Should().Be(WorkItemStatus.Completed);
        ActivityFormProgress.Read(item.FormProgressJson, steps, item.Id).Visits.Should().HaveCount(6);
        item = await _db.WorkItems.SingleAsync(w => w.WorkflowInstanceId == instance.Id && w.Status == WorkItemStatus.Pending);
        var otherItemId = item.Id; item.Claim(actor, now);
        ActivityFormProgress.Read(item.FormProgressJson, steps, item.Id).ActiveStepId.Should().Be("one");
        await Submit("activity"); // Return from B to A against the physical A -> B arrow.
        item.Status.Should().Be(WorkItemStatus.Completed);
        item = await _db.WorkItems.SingleAsync(w => w.WorkflowInstanceId == instance.Id && w.Status == WorkItemStatus.Pending);
        item.Id.Should().NotBe(firstItemId).And.NotBe(otherItemId);
        ActivityFormProgress.Read(item.FormProgressJson, steps, item.Id).ActiveStepId.Should().Be("one");
        item.Claim(actor, now);
        await Submit("finish"); // Explicit Otherwise exits even from the first form.
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Exactly(8));
    }

    [Fact]
    public void FormProgressMigration_MatchesTheModelAndOnlyAddsNullableStorage()
    {
        using var sql = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=NWFM;Trusted_Connection=True;TrustServerCertificate=True").Options, new StubTenant(_orgId));
        sql.Database.HasPendingModelChanges().Should().BeFalse();
        var script = sql.GetService<IMigrator>().GenerateScript("20260920044329_WorkflowWorkspace", "20261009120000_ActivityFormProgress");
        script.Should().Contain("ADD [FormProgressJson] nvarchar(max) NULL");
        script.Should().NotContain("DROP TABLE");
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("membership", "ACTIVITY_FORM_MEMBERSHIP")]
    [InlineData("unavailable", "ACTIVITY_FORM_UNAVAILABLE")]
    [InlineData("field", "ROUTING_CONDITION")]
    [InlineData("target", "ROUTING_DESTINATION")]
    [InlineData("review", "ROUTING_FORM_REVIEW")]
    [InlineData("loop", "ROUTING_NO_END")]
    public async Task Publication_ChecksEveryFormAndItsRoutes(string scenario, string? expected)
    {
        var type = Guid.NewGuid(); var form = Guid.NewGuid();
        var step = new ActivityFormStep { Id = "one", FormId = form, VersionNo = 1, Name = "First", ReviewRequired = scenario == "review" };
        if (scenario == "target") step.Otherwise = new() { Kind = "Form", Target = "removed" };
        if (scenario == "loop") step.Otherwise = new() { Kind = "Activity", Target = "activity" };
        if (scenario == "field") step.Rules.Add(new() { Id = "rule", Name = "Rule", When = new() { Conditions = [new() { Field = "removed", Operator = "equal", Value = JsonSerializer.SerializeToElement("x") }] } });
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new { taskTypeId = type, formSteps = new[] { step } }), DateTime.UtcNow);
        var versions = new WorkflowVersionRepository(_db); var version = (await versions.GetByIdWithProjectionAsync(seeded.Version))!;
        _db.Entry(version).Property(v => v.Status).CurrentValue = WorkflowVersionStatus.Draft;
        var types = new Mock<IWorkflowTaskTypeCatalog>(); types.Setup(t => t.IsActiveAsync(type, default)).ReturnsAsync(true);
        types.Setup(t => t.FormIdsAsync(type, default)).ReturnsAsync(scenario == "membership" ? [] : new[] { form });
        var forms = new Mock<IFormGateway>(); forms.Setup(f => f.FindPublishedAsync(form, default)).ReturnsAsync(new PublishedFormInfo(form, "FORM", "Form", "", "", "PUBLISHED", 1, scenario != "unavailable"));
        forms.Setup(f => f.GetVersionSchemaAsync(form, 1, default)).ReturnsAsync("{}"); forms.Setup(f => f.GetFieldsAsync(form, 1, default)).ReturnsAsync([]);
        var publisher = new WorkflowWorkspacePublisher(_db, Mock.Of<IWorkflowReferenceData>(), versions, Mock.Of<IOrgDirectory>(), types.Object, forms.Object);
        var issues = await publisher.ValidateAsync(version, default);
        if (expected is null) issues.Should().NotContain(i => i.Code.StartsWith("ROUTING_") || i.Code.StartsWith("ACTIVITY_FORM_"));
        else issues.Should().Contain(i => i.Code == expected && i.NodeKey == "activity");
    }

    [Fact]
    public async Task MainActivity_FormMovesPreserveChildAndSla_RestartCreatesFreshChild()
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var form = Guid.NewGuid(); var seeded = SeedWorkspace(now);
        var definition = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seeded.Version && a.ActivityType == ActivityType.MainActivity);
        var config = JsonSerializer.Deserialize<BusinessActivityConfiguration>(definition.ConfigurationJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        config.FormSteps = [new() { Id = "one", FormId = form, VersionNo = 1 }, new() { Id = "two", FormId = form, VersionNo = 1, Otherwise = new() { Kind = "Activity", Target = "activity" } }];
        config.FormSteps[0].Name = new string('x', 5000); // Larger than the SQL history payload column.
        _db.Entry(definition).Property(a => a.ConfigurationJson).CurrentValue = Config(config); await _db.SaveChangesAsync();
        var engine = BuildEngine(_db); var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "main-forms", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        var startEvent = await _db.WorkflowEvents.SingleAsync(e => e.WorkflowInstanceId == root.Id && e.EventType == WorkflowEventType.ActivityStarted && e.ActivityNodeKey == "activity");
        startEvent.PayloadJson!.Length.Should().BeLessThan(4000);
        using (var payload = JsonDocument.Parse(startEvent.PayloadJson))
        {
            payload.RootElement.GetProperty("workflowVersionId").GetGuid().Should().Be(seeded.Version);
            payload.RootElement.GetProperty("childWorkflowVersionId").GetGuid().Should().NotBeEmpty();
        }
        var childTimer = await _db.WorkflowTimers.SingleAsync(); await engine.ResumeFromTimerAsync(childTimer.Id, now.AddSeconds(5)); await engine.ResumeFromCallActivityAsync(root.Id, "activity", now.AddSeconds(5));
        var item = await _db.WorkItems.SingleAsync(); item.Claim(actor, now); var deadline = item.DueAt;
        var forms = new Mock<IFormGateway>(); forms.Setup(f => f.GetVersionSchemaAsync(form, 1, default)).ReturnsAsync("{}"); forms.Setup(f => f.GetFieldsAsync(form, 1, default)).ReturnsAsync([]);
        forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).ReturnsAsync(Result.Success(new FormSubmitReceipt(Guid.NewGuid(), 1, false)));
        forms.Setup(f => f.GetLatestByContextAsync(form, "WorkItem", It.IsAny<string>(), default)).ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), form, 1, actor.ToString(), "Actor", null, new Dictionary<string, object?>()));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var groups = new Mock<IWorkflowAssignmentGroupRepository>(); groups.Setup(g => g.IsUserMemberOfGroupAsync(item.AssignmentGroupId, actor, _orgId, default)).ReturnsAsync(true);
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine, new WorkflowEventAppender(new WorkflowEventRepository(_db)),
            Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(_db), new WorkflowVersionRepository(_db), new ActivityInstanceRepository(_db), groups.Object,
            Mock.Of<IWorkflowDepartmentRepository>(), _candidateFactory.Object, new WorkItemCandidateRepository(_db), forms.Object);
        (await handler.Handle(new(item.Id, actor, _orgId, "APPROVE", FormVisitId: item.Id), default)).IsSuccess.Should().BeTrue();
        item.Status.Should().Be(WorkItemStatus.Claimed); item.DueAt.Should().Be(deadline);
        (await _db.WorkflowInstances.CountAsync(i => i.ParentInstanceId == root.Id)).Should().Be(1);
        var progress = JsonSerializer.Deserialize<ActivityFormProgress>(item.FormProgressJson!)!;
        (await handler.Handle(new(item.Id, actor, _orgId, "APPROVE", FormVisitId: progress.VisitId), default)).IsSuccess.Should().BeTrue();
        (await _db.WorkflowInstances.CountAsync(i => i.ParentInstanceId == root.Id)).Should().Be(2);
        (await _db.ActivityInstances.CountAsync(a => a.WorkflowInstanceId == root.Id && a.Phase == "WaitingForChild")).Should().Be(1);
        (await _db.WorkItems.CountAsync(w => w.WorkflowInstanceId == root.Id)).Should().Be(1, "the fresh parent execution awaits its child");
    }

    [Theory]
    [InlineData("Next")]
    [InlineData("Return")]
    [InlineData("Activity")]
    [InlineData("Restart")]
    [InlineData("Reject")]
    public async Task FormSequence_SavesVisitsRoutesAndDoesNotDuplicateRetries(string scenario)
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var form = Guid.NewGuid();
        var steps = new List<ActivityFormStep>
        {
            new() { Id = "one", FormId = form, VersionNo = 1 },
            new() { Id = "two", FormId = form, VersionNo = 2, Otherwise = new() { Kind = scenario == "Return" ? "Form" : "Next", Target = "one" } },
            new() { Id = "three", FormId = form, VersionNo = 3 }
        };
        if (scenario is "Activity" or "Restart") steps[0].Otherwise = new() { Kind = "Activity", Target = scenario == "Restart" ? "activity" : "end" };
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new { formSteps = steps, rejectTargetNodeKey = "activity" }), now);
        var versions = new WorkflowVersionRepository(_db); var version = (await versions.GetByIdWithProjectionAsync(seeded.Version))!; version.SetWorkspace(null);
        var instance = WorkflowInstance.Start(_orgId, seeded.Binding.Id, version.Id, "sequence", "case", "activity", now);
        var execution = ActivityInstance.Start(_orgId, instance.Id, "activity", ActivityType.UserTask, "Activity", now);
        var item = WorkItem.Create(_orgId, instance.Id, execution.Id, Guid.NewGuid(), now); item.Claim(actor, now);
        _db.WorkflowInstances.Add(instance); _db.ActivityInstances.Add(execution); _db.WorkItems.Add(item); await _db.SaveChangesAsync();
        var forms = new Mock<IFormGateway>();
        forms.Setup(f => f.GetVersionSchemaAsync(form, It.IsAny<int>(), default)).ReturnsAsync("{}");
        forms.Setup(f => f.GetFieldsAsync(form, It.IsAny<int>(), default)).ReturnsAsync([]);
        forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).ReturnsAsync(Result.Success(new FormSubmitReceipt(Guid.NewGuid(), 1, false)));
        forms.Setup(f => f.GetLatestByContextAsync(form, "WorkItem", It.IsAny<string>(), default)).ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), form, 1, actor.ToString(), "Actor", null, new Dictionary<string, object?> { ["answer"] = "canonical" }));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var engine = BuildEngine(_db);
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine,
            new WorkflowEventAppender(new WorkflowEventRepository(_db)), Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(_db), versions,
            new ActivityInstanceRepository(_db), Mock.Of<IWorkflowAssignmentGroupRepository>(), Mock.Of<IWorkflowDepartmentRepository>(),
            Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), forms.Object);
        _assignmentResolver.Setup(a => a.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(item.AssignmentGroupId));
        _candidateFactory.Setup(c => c.CreateCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
        var action = scenario == "Reject" ? "REJECT" : "APPROVE";
        var command = new CompleteWorkItemCommand(item.Id, actor, _orgId, action, FormValues: new() { ["answer"] = JsonSerializer.SerializeToElement("client") }, FormVisitId: item.Id);
        var result = await handler.Handle(command, default);
        result.IsSuccess.Should().BeTrue();
        if (scenario == "Reject") { forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Never); return; }
        var progress = JsonSerializer.Deserialize<ActivityFormProgress>(item.FormProgressJson!)!;
        progress.Visits.Should().ContainSingle(); progress.Visits[0].Answers["answer"]!.ToString().Should().Be("canonical");
        if (scenario is "Activity" or "Restart")
        {
            item.Status.Should().Be(WorkItemStatus.Completed);
            if (scenario == "Activity") instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
            else (await _db.WorkItems.CountAsync(w => w.WorkflowInstanceId == instance.Id)).Should().Be(2);
        }
        else
        {
            item.Status.Should().Be(WorkItemStatus.Claimed); progress.ActiveStepId.Should().Be("two");
            (await _db.ActivityInstances.CountAsync(a => a.WorkflowInstanceId == instance.Id)).Should().Be(1);
            (await handler.Handle(command, default)).IsSuccess.Should().BeTrue();
            forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Once);
            (await handler.Handle(command with { FormVisitId = Guid.NewGuid() }, default)).Error.Code.Should().Be("Workflow.Form.StaleVisit");
            var next = command with { FormVisitId = progress.VisitId };
            (await handler.Handle(next, default)).IsSuccess.Should().BeTrue();
            progress = JsonSerializer.Deserialize<ActivityFormProgress>(item.FormProgressJson!)!;
            progress.ActiveStepId.Should().Be(scenario == "Return" ? "one" : "three"); progress.Visits.Should().HaveCount(2);
            if (scenario == "Next") (await handler.Handle(command with { FormVisitId = progress.VisitId }, default)).IsSuccess.Should().BeTrue();
            else progress.VisitId.Should().NotBe(item.Id);
        }
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        (await _db.WorkItems.FindAsync(item.Id))!.FormProgressJson.Should().NotBeNull();
    }
}
