namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Api.Controllers;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task RuntimeRejectsRecursiveCallsEvenForAnInvalidPersistedConfiguration()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var a = new CallableTarget(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity");
        var b = new CallableTarget(seed.Version.WorkflowDefinitionId, seed.Version.Id, "callee");
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = EmptyMapping() with { Callable = true, InvocationInputs = new("number") } }));
        var config = Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = EmptyMapping() with { Callable = true, InvocationInputs = new("number"), Actions = [new("recursive", a, "Run", Literal(1))] } });
        _db.Add(ActivityDefinition.Create(seed.Version.Id, b.NodeKey, ActivityType.ServiceTask, "B", now, actionKey: "http.request", configurationJson: config));
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "recursive", "key", "activity", now, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "A", now);
        var parent = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Graph", seed.Node.ConfigurationJson!, "a", now);
        parent.Complete("{}", now); parent.MarkGraphAdvanced();
        var call = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, b.NodeKey, "Callable", config, "b", now, parent.Id, "first", "1");
        var job = WorkflowIntegrationJob.Create(_orgId, root.Id, origin.Id, Guid.NewGuid(), "Http", config, "{}", "b"); job.AttachInvocation(call.Id, "Callable");
        _db.AddRange(root, origin, parent, call, job); await _db.SaveChangesAsync();
        var engine = new Mock<global::Workflow.Application.Abstractions.IWorkflowRuntimeEngine>(MockBehavior.Strict);
        await s.Runtime.ApplyDeliveryAsync(job, new IntegrationResult(true, 200, "{\"result\":7}", null), engine.Object, default);
        call.Status.Should().Be("Attention"); call.Error.Should().Contain("Recursive");
        (await _db.MappingInvocations.CountAsync()).Should().Be(2); origin.Status.Should().Be(ActivityInstanceStatus.Active); engine.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FollowUpCreationAndRecoveryKeepTheSameUnavailableTargetPolicy(bool wait)
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var childDefinition = WorkflowDefinition.Create(_orgId, "missing-child", "Child", now);
        var childVersion = WorkflowVersion.CreateDraft(childDefinition.Id, 1, actor, now);
        var targetConfig = Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = EmptyMapping() with { Callable = true, InvocationInputs = new("number") } });
        var target = ActivityDefinition.Create(childVersion.Id, "target", ActivityType.ServiceTask, "Target", now, actionKey: "http.request", configurationJson: targetConfig);
        var mapping = EmptyMapping() with { Actions = [new("missing", new(childDefinition.Id, childVersion.Id, "target"), "MapOnly", Literal(7), wait ? new("Wait", 59, "no-child") : null)] };
        var config = Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = mapping });
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "initial-policy", "key", "activity", now, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "A", now);
        var parent = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", config, "parent", now);
        var job = WorkflowIntegrationJob.Create(_orgId, root.Id, origin.Id, Guid.NewGuid(), "Http", config, "{}", "parent"); job.AttachInvocation(parent.Id, "Callable");
        _db.AddRange(childDefinition, childVersion, target, root, origin, parent, job); await _db.SaveChangesAsync();
        var engine = new Mock<global::Workflow.Application.Abstractions.IWorkflowRuntimeEngine>(MockBehavior.Strict);
        await s.Runtime.ApplyDeliveryAsync(job, new IntegrationResult(true, 200, "{\"result\":7}", null), engine.Object, default);
        parent.Applied.Should().BeTrue();
        var call = await _db.MappingInvocations.SingleAsync(i => i.ParentInvocationId == parent.Id);
        call.Status.Should().Be(wait ? "Waiting" : "Attention"); var deadline = call.Deadline;
        if (wait) { deadline.Should().Be(call.WindowAnchor.AddSeconds(59)); call.TimeoutOutcome.Should().Be("no-child"); }
        await MappingServices().Runtime.RecoverAsync(default);
        call.Status.Should().Be(wait ? "Waiting" : "Attention"); call.Deadline.Should().Be(deadline);
        (await _db.IntegrationJobs.CountAsync()).Should().Be(1); (await _db.WorkflowInstances.CountAsync()).Should().Be(1); engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RelatedRootCatalogPublicationAndRuntimeKeepPinnedChildFormsAndEventsInsideSelectedTree()
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var main = WorkflowDefinition.Create(_orgId, "related", "Related", now); var child = WorkflowDefinition.Create(_orgId, "related-child", "Related child", now);
        var rootVersion = WorkflowVersion.CreateDraft(main.Id, 1, actor, now); var childVersion = WorkflowVersion.CreateDraft(child.Id, 1, actor, now);
        var typeId = Guid.NewGuid(); var formId = Guid.NewGuid(); var submissionId = Guid.NewGuid(); var visitId = Guid.NewGuid();
        rootVersion.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4)));
        childVersion.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Child", "C", "B", DesignerVersion: 4) { ParentDefinitionId = main.Id, ParentNodeKey = "task", TaskTypeId = typeId }));
        rootVersion.Publish(actor, now); childVersion.Publish(actor, now);
        var task = ActivityDefinition.Create(rootVersion.Id, "task", ActivityType.MainActivity, "Task Type", now,
            configurationJson: Config(new BusinessActivityConfiguration { TaskTypeId = typeId, VersionId = childVersion.Id }));
        var stepConfig = new BusinessActivityConfiguration { TaskTypeId = typeId, FormSteps = [new() { Id = "form-step", FormId = formId, VersionNo = 2, Name = "Form" }] };
        var step = ActivityDefinition.Create(childVersion.Id, "step", ActivityType.UserTask, "Step", now, configurationJson: Config(stepConfig));
        var producer = ActivityDefinition.Create(childVersion.Id, "event", ActivityType.ServiceTask, "Event", now, actionKey: "http.request",
            configurationJson: Config(new HttpActivityConfiguration { MappingMode = "Typed", TypedMapping = EmptyMapping() with { Outputs = [new("a.b", "Total", new("number"), Literal(7))], Callable = true, InvocationInputs = new("number") } }));
        _db.AddRange(main, child, rootVersion, childVersion, task, step, producer); await _db.SaveChangesAsync();
        var types = new Mock<IWorkflowTaskTypeCatalog>(); types.Setup(t => t.FormIdsAsync(typeId, default)).ReturnsAsync([formId]);
        s.Forms.Setup(f => f.FindFieldActivityFormAsync(formId, default)).ReturnsAsync(new FieldActivityFormInfo(formId, "F", "Form", "Form", "", "Published", "D", "A", 2, [2], true, now));
        s.Forms.Setup(f => f.GetVersionSchemaAsync(formId, 2, default)).ReturnsAsync("{}");
        s.Forms.Setup(f => f.GetFieldsAsync(formId, 2, default)).ReturnsAsync([new("answer.key", "numeric", "Answer", null, null, [])]);
        var catalog = new DesignerMappingCatalog(_db, new WorkflowVersionRepository(_db), new StubTenant(_orgId), Mock.Of<ICurrentUser>(u => u.Id == actor.ToString()), s.Access.Object, s.Forms.Object, types.Object);
        var relatedCatalog = (await catalog.GetAsync(main.Id, rootVersion.Id, default)).Value;
        relatedCatalog.Sources.Should().Contain(x => x.Reference.FormId == formId && x.Reference.WorkflowVersionId == childVersion.Id);
        (await catalog.RelatedAsync(default)).Should().NotContain(x => x.VersionId == childVersion.Id);
        var eventSource = new SourceReference(child.Id, childVersion.Id, "event", "EventOutput", "a.b", SelectorId: "related");
        var formSource = new SourceReference(child.Id, childVersion.Id, "step", "FormField", "answer.key", FormStepId: "form-step", FormId: formId, FormVersion: 2, SelectorId: "related");
        var mapping = EmptyMapping() with
        {
            Selectors = [new("related", main.Id, rootVersion.Id, "BusinessKey", Literal("business-root"))],
            Inputs = [new("body", "$body", new("object", Properties: new() { ["text"] = new("text", Args: [new("source", Source: eventSource)]) }))]
        };
        seed.Node.SetPublishedConfiguration(Config(new HttpActivityConfiguration { MappingMode = "Typed", ConnectionId = Guid.NewGuid(), TypedMapping = mapping }));
        await _db.SaveChangesAsync();
        (await new MappingPublicationValidator(catalog).ValidateAsync(seed.Version, default)).Should().BeEmpty();
        var controller = new WorkflowMappingController(catalog); var owner = new CallableTarget(seed.Version.WorkflowDefinitionId, seed.Version.Id, "activity");
        var preview = (OkObjectResult)await controller.Preview(owner.WorkflowDefinitionId, owner.WorkflowVersionId, new(mapping, [new(eventSource, JsonSerializer.SerializeToElement(7))], owner), default);
        ((MappingPreviewResult)preview.Value!).Errors.Should().BeEmpty();
        var invalidAction = mapping with { Actions = [new("outside", new(child.Id, childVersion.Id, "event"), "Run", Literal(1))] };
        var validated = (OkObjectResult)await controller.Validate(owner.WorkflowDefinitionId, owner.WorkflowVersionId, new(owner, invalidAction), default);
        JsonSerializer.SerializeToElement(validated.Value).GetProperty("valid").GetBoolean().Should().BeFalse();

        var ownRoot = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "own", "own", "activity", now, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, ownRoot.Id, "activity", ActivityType.ServiceTask, "Origin", now);
        var selected = WorkflowInstance.Start(_orgId, seed.Binding.Id, rootVersion.Id, "selected", "business-root", "task", now, startedByUserId: actor);
        var alien = WorkflowInstance.Start(_orgId, seed.Binding.Id, rootVersion.Id, "alien", "other-key", "task", now, startedByUserId: actor);
        WorkflowInstance ChildInstance(WorkflowInstance parent, string key, int value)
        {
            var activation = ActivityInstance.Start(_orgId, parent.Id, "task", ActivityType.MainActivity, "Task", now);
            var instance = WorkflowInstance.Start(_orgId, seed.Binding.Id, childVersion.Id, key, "not-the-root-business-key", "event", now, startedByUserId: actor, parentInstanceId: parent.Id, parentActivityNodeKey: "task");
            instance.AttachParentActivity(activation.Id);
            var eventVisit = ActivityInstance.Start(_orgId, instance.Id, "event", ActivityType.ServiceTask, "Event", now);
            var result = WorkflowMappingInvocation.Create(_orgId, parent.Id, eventVisit.Id, null, actor, instance.Id, childVersion.Id, "event", "Graph", producer.ConfigurationJson!, key + "-receipt", now);
            result.Complete("{\"a.b\":" + value + "}", now); result.MarkGraphAdvanced();
            _db.AddRange(activation, instance, eventVisit, result); return instance;
        }
        var selectedChild = ChildInstance(selected, "child", 7); ChildInstance(alien, "alien-child", 99);
        var formActivity = ActivityInstance.Start(_orgId, selectedChild.Id, "step", ActivityType.UserTask, "Step", now);
        var item = WorkItem.Create(_orgId, selectedChild.Id, formActivity.Id, Guid.NewGuid(), now);
        item.SetFormProgress(JsonSerializer.Serialize(new ActivityFormProgress { ActiveStepId = "form-step", VisitId = visitId, ExitNodeKey = "done", Visits = [new() { VisitId = visitId, StepId = "form-step", FormId = formId, VersionNo = 2, SubmissionId = submissionId }] }));
        s.Forms.Setup(f => f.GetByClientIdAsync(formId, visitId, "WorkItem", item.Id + ":" + visitId, default)).ReturnsAsync(new FormSubmissionRecord(submissionId, formId, 2, actor.ToString(), "Actor", now, new Dictionary<string, object?> { ["answer.key"] = 13 }));
        var invocation = WorkflowMappingInvocation.Create(_orgId, ownRoot.Id, origin.Id, null, actor, ownRoot.Id, seed.Version.Id, "activity", "Graph", seed.Node.ConfigurationJson!, "consumer", now);
        _db.AddRange(ownRoot, origin, selected, alien, formActivity, item, invocation); await _db.SaveChangesAsync();
        var resolver = new MappingSourceResolver(_db, s.Access.Object, s.Forms.Object, types.Object);
        var criteria = MappingSelectorPreparation.Freeze(mapping.Selectors[0], [], now);
        (await resolver.SelectRelatedAsync(criteria, _orgId, actor, default))!.Id.Should().Be(selected.Id);
        var selections = new Dictionary<string, FrozenRelatedSelection> { ["related"] = new(selected.Id, JsonSerializer.SerializeToElement("business-root"), null, now, null, "Bound") };
        var context = new MappingResolutionContext(_orgId, actor, ownRoot.Id, invocation.Id, now, selections);
        var result = await resolver.ResolveAsync(eventSource, context, default); result.Value.GetInt32().Should().Be(7); result.InstanceId.Should().Be(selectedChild.Id);
        var form = await resolver.ResolveAsync(formSource, context, default); form.Value.GetInt32().Should().Be(13); form.SubmissionId.Should().Be(submissionId); form.FormVisitId.Should().Be(visitId);
        invocation.Bind("[]", Config(selections), Config(new[] { result, form })); await _db.SaveChangesAsync();
        ChildInstance(selected, "repeated-child", 88); await _db.SaveChangesAsync();
        (await resolver.ResolveAsync(eventSource, context, default)).Value.GetInt32().Should().Be(7);
        invocation.Bind("[]", Config(selections), "[]"); await _db.SaveChangesAsync();
        Func<Task> ambiguous = async () => await resolver.ResolveAsync(eventSource, context, default); await ambiguous.Should().ThrowAsync<FormatException>().WithMessage("*multiple visits*");
        Func<Task> wrongVersion = async () => await resolver.ResolveAsync(eventSource with { WorkflowDefinitionId = seed.Version.WorkflowDefinitionId, WorkflowVersionId = seed.Version.Id }, context, default);
        await wrongVersion.Should().ThrowAsync<UnauthorizedAccessException>();
        s.Access.Setup(a => a.GetAsync(actor, default)).ReturnsAsync(new WorkflowActorAccess([], OrgScopeSet.Unrestricted(), OrgHierarchy.Empty));
        Func<Task> revoked = async () => await resolver.ResolveAsync(formSource, context, default); await revoked.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundTargetRecoveryUsesFrozenActionPolicyWithoutExtendingDeadline(bool wait)
    {
        var s = MappingServices(); var seed = await TypedSeed(Guid.NewGuid()); var actor = Guid.NewGuid(); var now = DateTime.UtcNow;
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "policy", "key", "activity", now, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Origin", now);
        var policy = wait ? new MissingDataPolicy("Wait", 172800, "child-expired") : null;
        var mapping = EmptyMapping() with { Actions = [new("action", new(Guid.NewGuid(), Guid.NewGuid(), "target"), "Run", Literal(1), policy)] };
        var parent = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Graph", Config(new HttpActivityConfiguration { TypedMapping = mapping }), "parent", now);
        parent.Complete("{}", now); parent.MarkGraphAdvanced();
        var call = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, Guid.Empty, mapping.Actions[0].Target.WorkflowVersionId, "target", "Callable", seed.Node.ConfigurationJson!, "call", now, parent.Id, "action", "1");
        _db.AddRange(root, origin, parent, call); await _db.SaveChangesAsync();
        await s.Runtime.PrepareAsync(call, default);
        call.Status.Should().Be(wait ? "Waiting" : "Attention");
        if (wait)
        {
            call.Deadline.Should().Be(now.AddHours(48)); call.TimeoutOutcome.Should().Be("child-expired");
            var recovered = MappingServices(); await recovered.Runtime.RecoverAsync(default); await recovered.Runtime.RecoverAsync(default);
            call.Deadline.Should().Be(now.AddHours(48)); call.TimeoutOutcome.Should().Be("child-expired"); call.Status.Should().Be("Waiting");
        }
        (await _db.IntegrationJobs.CountAsync()).Should().Be(0); (await _db.WorkflowInstances.CountAsync()).Should().Be(1);
    }
}
