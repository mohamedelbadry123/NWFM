namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Application.Forms.Common;
using global::FormEngine.Application.Submissions.Common;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ClaimWorkItem;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task IntegrationWorkspaceCompletionUsesEffectiveActorAndKeepsAudit(bool multi, bool demo, bool startup)
    {
        var now = DateTime.UtcNow; var claimant = Guid.NewGuid(); var admin = demo ? Guid.NewGuid() : claimant;
        using var formsDb = global::NWFM.Tests.Modules.FormEngine.FormEngineTestData.CreateContext();
        var store = new Mock<IFormSubmissionStore>(); var keys = new Dictionary<Guid, Guid>(); var rows = new Dictionary<Guid, IReadOnlyDictionary<string, object?>>();
        var publisher = new FormPublisher(formsDb, store.Object, Mock.Of<NWFM.Shared.Caching.ICacheService>(), TimeProvider.System);
        var form = FormDefinition.Create("ACTOR", "Actor", "Actor", FormCategories.Inspection, null, null, "test", now);
        form.SetSchema(global::NWFM.Tests.Modules.FormEngine.FormEngineTestData.SimpleSchema(), null, null, "test", now); formsDb.FormDefinitions.Add(form); await formsDb.SaveChangesAsync();
        (await publisher.PublishAsync(form.Id, "test", default)).IsSuccess.Should().BeTrue();
        store.Setup(s => s.FindByClientIdAsync(It.IsAny<FormTable>(), It.IsAny<Guid>(), default)).ReturnsAsync((FormTable _, Guid id, CancellationToken _) => keys.TryGetValue(id, out var receipt) ? (Guid?)receipt : null);
        store.Setup(s => s.GetByIdAsync(It.IsAny<FormTable>(), It.IsAny<Guid>(), default)).ReturnsAsync((FormTable _, Guid id, CancellationToken _) => rows.GetValueOrDefault(id));
        store.Setup(s => s.InsertAsync(It.IsAny<FormTable>(), It.IsAny<FormSubmissionInsert>(), default)).ReturnsAsync((FormTable _, FormSubmissionInsert input, CancellationToken _) =>
        {
            var id = Guid.NewGuid(); keys.Add(input.ClientSubmissionId!.Value, id);
            rows.Add(id, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["VersionNo"] = input.VersionNo,
                ["ContextType"] = input.ContextType,
                ["ContextId"] = input.ContextId,
                ["SubmittedBy"] = input.SubmittedBy,
                ["SubmittedDate"] = DateTimeOffset.UtcNow,
                ["meter_reading"] = input.Answers["meter_reading"]
            });
            return id;
        });
        var config = new BusinessActivityConfiguration
        {
            FormDefinitionId = multi ? null : form.Id,
            FormVersionNo = multi ? null : 1,
            FormSteps = multi ? [new() { Id = "first", FormId = form.Id, VersionNo = 1 }, new() { Id = "second", FormId = form.Id, VersionNo = 1 }] : []
        };
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(config), now); var versions = new WorkflowVersionRepository(_db);
        var version = (await versions.GetByIdWithProjectionAsync(seed.Version))!; version.SetWorkspace(null);
        var participant = WorkflowParticipant.Create(_orgId, claimant, "Participant", "participant@example.test", now); if (demo) participant.MarkDemo();
        _db.Add(participant);
        _db.Add(ActivityOutcomeDefinition.Create(version.Id, version.Activities.Single(a => a.NodeKey == "activity").Id, "APPROVE", "Approve", 0, now));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, version.Id, "actor", "key", "activity", now); root.SetExecutionContext(OrgLocation.Empty, demo);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.UserTask, "Step", now);
        var item = WorkItem.Create(_orgId, root.Id, activity.Id, Guid.NewGuid(), now); _db.AddRange(root, activity, item); await _db.SaveChangesAsync();
        var attribution = new TrustedFormSubmissionActor(); var user = new Mock<NWFM.Shared.Abstractions.ICurrentUser>(); user.SetupGet(u => u.Id).Returns(admin.ToString()); user.SetupGet(u => u.UserName).Returns("Authenticated actor");
        var startupActor = NWFM.Tests.Api.StartupActorFixture.Create(admin);
        var service = new FormSubmissionService(formsDb, store.Object, publisher, Mock.Of<NWFM.Shared.Storage.IFileStorage>(), startup ? startupActor.User : user.Object, TimeProvider.System,
            Options.Create(new NWFM.Shared.Options.FileStorageOptions()), Options.Create(new global::FormEngine.Domain.Options.FormEngineOptions()), [new WorkflowFormSubmissionGuard(_db)], attribution);
        var gateway = new global::FormEngine.Infrastructure.Integration.FormGateway(formsDb, store.Object, service, Mock.Of<NWFM.Shared.Storage.IFileStorage>());
        var request = new FormSubmissionRequest
        {
            FormDefinitionId = form.Id,
            VersionNo = 1,
            ContextType = "WorkItem",
            ContextId = multi ? item.Id + ":" + item.Id : item.Id.ToString(),
            ClientSubmissionId = item.Id,
            Answers = new Dictionary<string, object?> { ["meter_reading"] = 999 }
        };
        (await service.SubmitAsync(request, default)).IsFailure.Should().BeTrue();
        store.Verify(s => s.InsertAsync(It.IsAny<FormTable>(), It.IsAny<FormSubmissionInsert>(), default), Times.Never);
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.AdvanceAsync(root.Id, item.Id, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var groupRepo = new Mock<IWorkflowAssignmentGroupRepository>(); groupRepo.Setup(g => g.IsUserMemberOfGroupAsync(item.AssignmentGroupId, claimant, _orgId, default)).ReturnsAsync(true);
        var appender = new WorkflowEventAppender(new WorkflowEventRepository(_db));
        var claim = new ClaimWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), groupRepo.Object, new WorkflowParticipantRepository(_db), appender, Mock.Of<IWorkItemDtoAssembler>(), Mock.Of<IWorkflowRequestProjector>());
        var complete = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine.Object, appender, Mock.Of<IWorkItemDtoAssembler>(),
            new WorkflowInstanceRepository(_db), versions, new ActivityInstanceRepository(_db), groupRepo.Object, Mock.Of<IWorkflowDepartmentRepository>(), Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), gateway);
        var sender = new Mock<MediatR.ISender>();
        sender.Setup(s => s.Send(It.IsAny<ClaimWorkItemCommand>(), default)).Returns((ClaimWorkItemCommand c, CancellationToken ct) => claim.Handle(c, ct));
        sender.Setup(s => s.Send(It.IsAny<CompleteWorkItemCommand>(), default)).Returns((CompleteWorkItemCommand c, CancellationToken ct) => complete.Handle(c, ct));
        var groups = new Mock<IWorkflowGroupDirectory>(); groups.Setup(g => g.IsMemberAsync(_orgId, item.AssignmentGroupId, claimant, default)).ReturnsAsync(true);
        var scopes = new Mock<IOrgScopeProvider>(); scopes.Setup(s => s.GetCurrentUserScopeAsync(default)).ReturnsAsync(OrgScopeSet.Unrestricted());
        var workspace = new WorkflowWorkspace(_db, new StubTenant(_orgId), sender.Object, engine.Object, groups.Object, Mock.Of<IWorkItemDtoAssembler>(), versions,
            new WorkflowActivityEvents(_db, MappingServices().Api), scopes.Object, Mock.Of<IOrgDirectory>(), formSubmissionActor: attribution);
        var input = new WorkspaceActionInput(Guid.NewGuid(), "APPROVE", FormValues: new() { ["meter_reading"] = JsonSerializer.SerializeToElement(42) }, DemoActorId: demo ? claimant : null, FormVisitId: multi ? item.Id : null);
        async Task CompleteAndVerify()
        {
            if (demo) (await workspace.ActAsync(item.Id, input, admin, false, default)).IsFailure.Should().BeTrue();
            var result = await workspace.ActAsync(item.Id, input, admin, demo, default);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
            (await workspace.ActAsync(item.Id, input, admin, demo, default)).IsSuccess.Should().BeTrue(); keys.Should().ContainSingle();
            var receipt = await gateway.GetByClientIdAsync(form.Id, item.FormReceiptKey(item.Id, claimant), "WorkItem", request.ContextId!, default);
            receipt!.SubmittedBy.Should().Be(claimant.ToString()); JsonSerializer.SerializeToElement(receipt.Answers["meter_reading"]).GetInt32().Should().Be(42);
            if (multi)
            {
                var visit = ActivityFormProgress.Read(item.FormProgressJson, config.FormSteps, item.Id).VisitId;
                (await workspace.ActAsync(item.Id, input with { RequestId = Guid.NewGuid(), FormVisitId = visit }, admin, demo, default)).IsSuccess.Should().BeTrue(); keys.Should().HaveCount(2);
            }
            item.Status.Should().Be(WorkItemStatus.Completed); item.CompletedByUserId.Should().Be(claimant);
            (await _db.WorkspaceActions.ToListAsync()).Should().HaveCount(multi ? 2 : 1).And.OnlyContain(a => a.ActorId == admin && a.EffectiveActorId == claimant);
            if (demo)
            {
                (await _db.WorkflowEvents.Where(e => e.EventType == WorkflowEventType.DemoAction).ToListAsync()).Should().HaveCount(multi ? 2 : 1)
                    .And.OnlyContain(e => e.ActorUserId == admin && e.PayloadJson!.Contains(claimant.ToString()));
                (await service.SubmitAsync(request, default)).IsFailure.Should().BeTrue(); // No ambient administrator bypass after the trusted scope exits.
                store.Verify(s => s.InsertAsync(It.IsAny<FormTable>(), It.Is<FormSubmissionInsert>(i => i.SubmittedBy == claimant.ToString() && i.SubmittedByName == "Participant"), default), Times.Exactly(multi ? 2 : 1));
            }
            else (await service.SubmitAsync(request with { ClientSubmissionId = item.FormReceiptKey(item.Id, claimant) }, default)).Value.IsReplay.Should().BeTrue();
            attribution.Resolve("WorkItem", item.Id.ToString(), admin.ToString()).Should().BeNull();
            store.Verify(s => s.GetLatestByContextAsync(It.IsAny<FormTable>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        }
        if (startup)
        {
            startupActor.Http.HttpContext.Should().BeNull(); startupActor.User.Id.Should().BeNull();
            await startupActor.Context.RunWorkspaceDemoAsync(async actor =>
            {
                actor.Should().Be(admin);
                // Even inside startup, an anonymous HTTP request cannot borrow the trusted principal.
                startupActor.Http.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                (await service.SubmitAsync(request, default)).IsFailure.Should().BeTrue();
                startupActor.Http.HttpContext = null;
                await CompleteAndVerify();
            });
            startupActor.User.Id.Should().BeNull();
            (await service.SubmitAsync(request, default)).IsFailure.Should().BeTrue();
        }
        else await CompleteAndVerify();
    }

    [Theory]
    [InlineData("Completed", "Latest", true)]
    [InlineData("Completed", "Attention", true)]
    [InlineData("Cancelled", "Latest", true)]
    [InlineData("Suspended", "Latest", true)]
    [InlineData("Running", "Latest", true)]
    [InlineData("Failed", "Latest", true)]
    [InlineData("Completed", "Latest", false)]
    [InlineData("Running", "Attention", false)]
    public async Task IntegrationSelectorStatusEligibilityPrecedesOrderingAfterRestart(string status, string multiple, bool hasTimely)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = await TypedSeed(connection); var anchor = DateTime.UtcNow.AddSeconds(-25); var actor = Guid.NewGuid();
        var mapping = EmptyMapping() with { Selectors = [new("prior", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", Literal("customer"), Status: status, MultipleMatches: multiple, Missing: new("Wait", 10, "expired"))] };
        var owner = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "owner", "customer", "activity", anchor, startedByUserId: actor);
        var activity = ActivityInstance.Start(_orgId, owner.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var invocation = WorkflowMappingInvocation.Create(_orgId, owner.Id, activity.Id, null, actor, owner.Id, seed.Version.Id, "activity", "Callable",
            Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "status-deadline", anchor);
        _db.AddRange(owner, activity, invocation); await _db.SaveChangesAsync(); await s.Runtime.PrepareAsync(invocation, default);
        invocation.Status.Should().Be("Waiting"); var frozen = invocation.CriteriaJson;
        var timely = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "X", "customer", "activity", anchor.AddSeconds(2));
        var late = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "Y", "customer", "activity", anchor.AddSeconds(5));
        void Enter(WorkflowInstance i, DateTime at)
        {
            switch (status)
            {
                case "Completed": i.Complete(at); break;
                case "Cancelled": i.Cancel(at); break;
                case "Suspended": i.Suspend(at); break;
                case "Failed": i.Fail("recorded", at); break;
                case "Running": i.Suspend(i.StartedAt.AddSeconds(1)); i.Resume(at); break;
            }
        }
        Enter(timely, anchor.AddSeconds(8)); Enter(late, anchor.AddSeconds(20));
        // Unrelated graph progress does not move the eligibility timestamp.
        timely.AdvanceTo("activity", anchor.AddSeconds(24));
        if (hasTimely) _db.Add(timely);
        _db.Add(late); await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await MappingServices().Runtime.RecoverAsync(default); invocation = await _db.MappingInvocations.SingleAsync();
        if (!hasTimely)
        {
            invocation.Status.Should().Be("Attention"); invocation.CriteriaJson.Should().Be(frozen);
            (await _db.IntegrationJobs.CountAsync()).Should().Be(0); local.Bodies.Should().BeEmpty(); return;
        }
        invocation.Status.Should().Be("Prepared"); invocation.CriteriaJson.Should().Be(frozen);
        var selection = JsonSerializer.Deserialize<Dictionary<string, FrozenRelatedSelection>>(invocation.SelectionsJson, IntegrationJson.Options)!["prior"];
        selection.InstanceId.Should().Be(timely.Id); selection.EligibleAt.Should().Be(new DateTimeOffset(anchor.AddSeconds(8)));
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: s.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        local.Bodies.Should().ContainSingle();
    }

    [Fact]
    public async Task IntegrationBoundSelectorKeepsOriginalStatusEvidenceWhileAnotherDependencyWaits()
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var connection = await Connection(s.Api, "Http", local.Address, "None");
        var seed = await TypedSeed(connection); var anchor = DateTime.UtcNow.AddSeconds(-25); var actor = Guid.NewGuid();
        var mapping = EmptyMapping() with
        {
            Selectors = [
            new("first", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", Literal("first"), Status: "Running", Missing: new("Wait", 10, "expired")),
            new("next", seed.Version.WorkflowDefinitionId, seed.Version.Id, "BusinessKey", Literal("next"), Missing: new("Wait", 60, "expired"))]
        };
        var owner = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "owner", "owner", "activity", anchor, startedByUserId: actor);
        var first = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "first", "first", "activity", anchor.AddSeconds(5));
        var activity = ActivityInstance.Start(_orgId, owner.Id, "activity", ActivityType.ServiceTask, "Event", anchor);
        var invocation = WorkflowMappingInvocation.Create(_orgId, owner.Id, activity.Id, null, actor, owner.Id, seed.Version.Id, "activity", "Callable",
            Config(new HttpActivityConfiguration { ConnectionId = connection, MappingMode = "Typed", TypedMapping = mapping }), "bound-status", anchor);
        _db.AddRange(owner, first, activity, invocation); await _db.SaveChangesAsync(); await s.Runtime.PrepareAsync(invocation, default);
        invocation.Status.Should().Be("Waiting");
        var selection = JsonSerializer.Deserialize<Dictionary<string, FrozenRelatedSelection>>(invocation.SelectionsJson, IntegrationJson.Options)!["first"];
        selection.InstanceId.Should().Be(first.Id); selection.EligibleAt.Should().Be(new DateTimeOffset(anchor.AddSeconds(5)));
        first.Suspend(anchor.AddSeconds(20));
        _db.Add(WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "next", "next", "activity", anchor.AddSeconds(8)));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); await MappingServices().Runtime.RecoverAsync(default);
        invocation = await _db.MappingInvocations.SingleAsync(); invocation.Status.Should().Be("Prepared");
        var recovered = JsonSerializer.Deserialize<Dictionary<string, FrozenRelatedSelection>>(invocation.SelectionsJson, IntegrationJson.Options)!["first"];
        JsonSerializer.Serialize(recovered).Should().Be(JsonSerializer.Serialize(selection));
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: s.Runtime).ProcessJobAsync((await _db.IntegrationJobs.SingleAsync()).Id, default);
        local.Bodies.Should().ContainSingle();
    }
}
