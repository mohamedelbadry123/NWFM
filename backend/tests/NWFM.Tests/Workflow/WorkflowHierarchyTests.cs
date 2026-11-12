namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Helpers;
using global::Workflow.Application.Integrations;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task Hierarchy_RefusesStandaloneChildCreation()
    {
        var result = await Workspace(NWFM.Shared.Organization.OrgScopeSet.Unrestricted())
            .CreateAsync(new("Child", null, new("Child", DesignerVersion: 3)), Guid.NewGuid(), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("inside the main workflow");
        (await _db.WorkflowDefinitions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hierarchy_ReopensTheOwnedPinnedChild_EvenAfterRetirement(bool retired)
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var parent = (await _db.WorkflowVersions.FindAsync(seeded.Version))!;
        var typeId = Guid.NewGuid();
        var parentSettings = new WorkflowWorkspaceDefinition("Main", DesignerVersion: 3)
        {
            SchemaVersion = 2,
            OrganizationScopes = [new(OrgLevels.Cluster, "CC", "CC")]
        };
        parent.SetWorkspace(Config(parentSettings));
        var task = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == parent.Id && a.ActivityType == ActivityType.MainActivity);
        var configuration = System.Text.Json.Nodes.JsonNode.Parse(task.ConfigurationJson!)!;
        configuration["taskTypeId"] = typeId;
        _db.Entry(task).Property(a => a.ConfigurationJson).CurrentValue = configuration.ToJsonString();
        var child = (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!;
        child.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Child", DesignerVersion: 3)
        {
            SchemaVersion = 2,
            OrganizationScopes = parentSettings.OrganizationScopes,
            TaskTypeId = typeId,
            ParentDefinitionId = parent.WorkflowDefinitionId,
            ParentNodeKey = "activity"
        }));
        if (retired)
        {
            child.Retire(now);
            (await _db.WorkflowDefinitions.FindAsync(child.WorkflowDefinitionId))!.Deactivate(now);
            (await _db.WorkflowDefinitions.FindAsync(parent.WorkflowDefinitionId))!.Deactivate(now);
        }
        await _db.SaveChangesAsync();
        var catalog = new Mock<IWorkflowTaskTypeCatalog>();
        catalog.Setup(c => c.IsActiveAsync(typeId, It.IsAny<CancellationToken>())).ReturnsAsync(!retired);
        var workspace = new WorkflowWorkspace(_db, new StubTenant(_orgId), null!, BuildEngine(_db),
            Mock.Of<IWorkflowGroupDirectory>(), null!, new WorkflowVersionRepository(_db), new WorkflowActivityEvents(_db, Integrations()),
            Mock.Of<IOrgScopeProvider>(), Mock.Of<IOrgDirectory>(), taskTypes: catalog.Object);
        var first = await workspace.OpenChildAsync(new(parent.Id, "activity"), Guid.NewGuid(), default);
        var second = await workspace.OpenChildAsync(new(parent.Id, "activity"), Guid.NewGuid(), default);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : "");
        second.Value.VersionId.Should().Be(seeded.ChildVersion);
        first.Value.Id.Should().Be(child.WorkflowDefinitionId);
        (await _db.WorkflowDefinitions.CountAsync()).Should().Be(2);
        var wrongOwner = WorkflowWorkspaceDefinition.Read(child.WorkspaceJson)! with { ParentNodeKey = "someone-else" };
        child.SetWorkspace(Config(wrongOwner));
        await _db.SaveChangesAsync();
        (await workspace.OpenChildAsync(new(parent.Id, "activity"), Guid.NewGuid(), default)).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false, "STEP_SINGLE_FORM")]
    [InlineData(2, false, "STEP_SINGLE_FORM")]
    [InlineData(1, true, "STEP_TASK_TYPE")]
    [InlineData(1, false, null)]
    public async Task Hierarchy_StepRequiresOneFormFromItsOwningTaskType(int formCount, bool differentType, string? expected)
    {
        var taskType = Guid.NewGuid();
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new
        {
            taskTypeId = differentType ? Guid.NewGuid() : taskType,
            formSteps = Enumerable.Range(0, formCount).Select(i => new { id = "form" + i, formId = Guid.NewGuid(), versionNo = 1 }),
        }), DateTime.UtcNow);
        var version = (await new WorkflowVersionRepository(_db).GetByIdWithProjectionAsync(seeded.Version))!;
        var settings = new WorkflowWorkspaceDefinition("Child", DesignerVersion: 3)
        { TaskTypeId = taskType, ParentDefinitionId = Guid.NewGuid(), ParentNodeKey = "meter" };
        var issues = new List<WorkflowValidationIssueDto>();
        WorkflowWorkspacePublisher.ValidateHierarchy(version, settings, issues);
        if (expected is null) issues.Should().BeEmpty();
        else issues.Should().Contain(i => i.Code == expected);
    }

    [Fact]
    public async Task Hierarchy_MainWorkflowRejectsSteps()
    {
        var seeded = SeedSingleActivity(ActivityType.UserTask, "{}", DateTime.UtcNow);
        var version = (await new WorkflowVersionRepository(_db).GetByIdWithProjectionAsync(seeded.Version))!;
        var issues = new List<WorkflowValidationIssueDto>();
        WorkflowWorkspacePublisher.ValidateHierarchy(version, new("Main", DesignerVersion: 3), issues);
        issues.Should().Contain(i => i.Code == "MAIN_STEP");
    }

    [Theory]
    [InlineData("Sync", false, "EVENT_SYNC_REQUIRED")]
    [InlineData("Async", false, null)]
    [InlineData("Other", true, "EVENT_MODE")]
    public async Task Hierarchy_ValidatesEventExecutionMode(string executionMode, bool required, string? expected)
    {
        var seeded = SeedSingleActivity(ActivityType.ServiceTask, Config(new { executionMode, required }), DateTime.UtcNow);
        var version = (await new WorkflowVersionRepository(_db).GetByIdWithProjectionAsync(seeded.Version))!;
        var issues = new List<WorkflowValidationIssueDto>();
        WorkflowWorkspacePublisher.ValidateHierarchy(version, new("Main", DesignerVersion: 3), issues);
        if (expected is null) issues.Should().BeEmpty();
        else issues.Should().Contain(i => i.Code == expected);
    }

    [Fact]
    public async Task Hierarchy_CompletedChildAdvancesParentOnceWithoutAnotherApprovalTask()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var version = (await _db.WorkflowVersions.FindAsync(seeded.Version))!;
        var settings = System.Text.Json.Nodes.JsonNode.Parse(version.WorkspaceJson!)!;
        settings["designerVersion"] = 3;
        version.SetWorkspace(settings.ToJsonString());
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db);
        var started = await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "hierarchy", now, pinnedWorkflowVersionId: seeded.Version);
        started.IsSuccess.Should().BeTrue();
        await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now.AddMinutes(1));
        await engine.ResumeFromCallActivityAsync(started.Value.Id, "activity", now.AddMinutes(1));
        await engine.ResumeFromCallActivityAsync(started.Value.Id, "activity", now.AddMinutes(2));
        started.Value.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkItems.CountAsync()).Should().Be(0);
        (await _db.ActivityInstances.CountAsync(a => a.WorkflowInstanceId == started.Value.Id && a.ActivityType == ActivityType.End)).Should().Be(1);
    }

    [Fact]
    public async Task Hierarchy_CompletionEventBlocksParentUntilResponseIsApplied_AndResumesOnce()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var version = (await _db.WorkflowVersions.FindAsync(seeded.Version))!;
        var settings = System.Text.Json.Nodes.JsonNode.Parse(version.WorkspaceJson!)!;
        settings["designerVersion"] = 3;
        version.SetWorkspace(settings.ToJsonString());
        var integrations = Integrations();
        var connection = await Connection(integrations, "Http", "http://127.0.0.1:5091", "None");
        _db.ActivityDefinitions.Add(ActivityDefinition.Create(seeded.Version, "release", ActivityType.ServiceTask, "Release", now,
            actionKey: "http.request", configurationJson: Config(new
            {
                connectionId = connection,
                required = true,
                executionMode = "Sync",
                triggerBinding = new { sourceNodeKey = "activity", trigger = "OnComplete" },
                outputMappings = new { releaseId = "body.id" }
            })));
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db, integrations);
        var instance = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "completion-event", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now.AddMinutes(1));
        await engine.ResumeFromCallActivityAsync(instance.Id, "activity", now.AddMinutes(1));
        var execution = await _db.ActivityInstances.SingleAsync(a => a.WorkflowInstanceId == instance.Id && a.ActivityType == ActivityType.MainActivity);
        execution.Phase.Should().Be("WaitingForTaskTypeEvents");
        await engine.ResumeActivityEventsAsync(execution.Id, default);
        instance.Status.Should().Be(WorkflowInstanceStatus.Running);
        (await _db.WorkItems.CountAsync()).Should().Be(0);
        var job = await _db.IntegrationJobs.SingleAsync();
        job.RecordDelivery(Config(new IntegrationResult(true, 200, "{\"id\":\"released\"}", null)), 200);
        await _db.SaveChangesAsync();
        await new WorkflowIntegrationProcessor(_db, integrations, new WorkflowIntegrationTransport(), engine).ProcessJobAsync(job.Id, default);
        await engine.ResumeActivityEventsAsync(execution.Id, default);
        await engine.ResumeActivityEventsAsync(execution.Id, default);
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        (await _db.WorkflowVariables.SingleAsync(v => v.WorkflowInstanceId == instance.Id && v.VariableName == "releaseId")).ValueJson.Should().Be("\"released\"");
        (await _db.IntegrationJobs.CountAsync()).Should().Be(1);
        (await _db.ActivityInstances.CountAsync(a => a.WorkflowInstanceId == instance.Id && a.ActivityType == ActivityType.End)).Should().Be(1);
    }

    [Theory]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(2, false, false)]
    public async Task Hierarchy_FormCanRouteToSequenceEvents_WithoutJumpingIntoTriggeredEvents(int designerVersion, bool triggered, bool allowed)
    {
        var now = DateTime.UtcNow;
        var seeded = SeedSingleActivity(ActivityType.UserTask, "{}", now);
        var version = (await _db.WorkflowVersions.FindAsync(seeded.Version))!;
        version.SetWorkspace(Config(new { kind = "Child", designerVersion }));
        _db.ActivityDefinitions.Add(ActivityDefinition.Create(seeded.Version, "release", ActivityType.ServiceTask, "Release", now,
            configurationJson: triggered ? Config(new { triggerBinding = new { sourceNodeKey = "activity", trigger = "OnComplete" } }) : "{}"));
        await _db.SaveChangesAsync();
        version = (await new WorkflowVersionRepository(_db).GetByIdWithProjectionAsync(seeded.Version))!;
        var step = new ActivityFormStep { Id = "registration" };
        var route = () => WorkflowFormRouting.ResolveDestination([step], step, new() { Kind = "Activity", Target = "release" }, version, "activity");
        if (allowed) route().NodeKey.Should().Be("release");
        else route.Should().Throw<FormatException>();
    }
}
