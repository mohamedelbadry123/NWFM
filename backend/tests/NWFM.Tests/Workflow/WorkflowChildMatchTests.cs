namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData("exact", true)]
    [InlineData("cluster", false)]
    [InlineData("branch", false)]
    [InlineData("extra", false)]
    [InlineData("type", false)]
    [InlineData("inactive", false)]
    public async Task Publication_RequiresExactChildScopeAndActiveActivityTaskType(string scenario, bool allowed)
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        var repo = new WorkflowVersionRepository(_db);
        var parent = (await repo.GetByIdWithProjectionAsync(seeded.Version))!;
        var child = (await repo.GetByIdWithProjectionAsync(seeded.ChildVersion))!;
        _db.Entry(parent).Property(v => v.Status).CurrentValue = WorkflowVersionStatus.Draft;
        var taskId = Guid.NewGuid();
        var scope = new WorkflowOrganizationScope("Cbu", "R1", "CC", "R1");
        var field = new WorkflowLookupItem(Guid.NewGuid(), "REPAIR", "Repair", "", "WATER");
        var settings = new WorkflowWorkspaceDefinition("Main") { SchemaVersion = 2, OrganizationScopes = [scope], FieldActivityTypeId = field.Id, DepartmentCode = field.ParentCode, FieldActivityCode = field.Code };
        parent.SetWorkspace(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var childSettings = settings with { Kind = "Child", TaskTypeId = scenario == "type" ? Guid.NewGuid() : taskId,
            OrganizationScopes = scenario switch {
                "cluster" => [new("Cluster", "CC", "CC")],
                "branch" => [new("Branch", "C1", "CC", "R1")],
                "extra" => [scope, new("Cluster", "OTHER", "OTHER")],
                _ => [scope]
            } };
        child.SetWorkspace(JsonSerializer.Serialize(childSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var node = parent.Activities.Single(a => a.ActivityType == ActivityType.MainActivity);
        _db.ActivityOutcomeDefinitions.Add(ActivityOutcomeDefinition.Create(parent.Id, node.Id, "approve", "Approve", 1, DateTime.UtcNow));
        var config = JsonNode.Parse(node.ConfigurationJson!)!;
        config["taskTypeId"] = taskId.ToString();
        node.SetPublishedConfiguration(config.ToJsonString());
        await _db.SaveChangesAsync();
        var tasks = new Mock<IWorkflowTaskTypeCatalog>();
        tasks.Setup(t => t.IsActiveAsync(taskId, It.IsAny<CancellationToken>())).ReturnsAsync(scenario != "inactive");
        var references = new Mock<IWorkflowReferenceData>();
        references.Setup(r => r.ListAsync("field-activity-types", null, It.IsAny<CancellationToken>())).ReturnsAsync([field]);
        references.Setup(r => r.IsValidFieldActivityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var directory = new Mock<IOrgDirectory>();
        directory.Setup(d => d.IsValidLocationAsync(It.IsAny<NWFM.Shared.Organization.OrgLocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var publisher = new WorkflowWorkspacePublisher(_db, references.Object, repo, directory.Object, tasks.Object);
        var issues = await publisher.ValidateAsync(parent, default);
        issues.Any(i => i.Code is "MAIN_CHILD_MATCH" or "MAIN_TASK_TYPE").Should().Be(!allowed);
        if (!allowed) (await publisher.PreparePublicationAsync(parent, default)).IsFailure.Should().BeTrue();
        else (await publisher.PreparePublicationAsync(parent, default)).IsSuccess.Should().BeTrue(string.Join(", ", issues.Select(i=>i.Message)));
    }

    [Fact]
    public void ExactScope_IgnoresOrderAndDuplicatesButPreservesLevelsAndAncestry()
    {
        var a = new WorkflowOrganizationScope("Cbu", "RCBU", "CC", "RCBU");
        var b = new WorkflowOrganizationScope("OperationArea", "OA", "EC", "E1");
        var parent = new WorkflowWorkspaceDefinition("Main") { SchemaVersion = 2, OrganizationScopes = [a,b] };
        WorkflowChildMatch.SameScope(parent, parent with { OrganizationScopes = [b,a,a] }).Should().BeTrue();
        WorkflowChildMatch.SameScope(parent, parent with { OrganizationScopes = [a] }).Should().BeFalse();
        WorkflowChildMatch.SameScope(parent, parent with { OrganizationScopes = [a,b with { ClusterCode = "OTHER" }] }).Should().BeFalse();
        WorkflowChildMatch.SameScope(parent, parent with { OrganizationScopes = [a,b with { Level = "Branch" }] }).Should().BeFalse();
        WorkflowChildMatch.SameScope(parent, new("Child")).Should().BeFalse();
    }

    [Fact]
    public async Task ChildCatalog_PreservesSelectedPublishedVersionAlongsideLatest()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        var previous = (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!;
        var next = WorkflowVersion.CreateDraft(previous.WorkflowDefinitionId, 2, Guid.NewGuid(), DateTime.UtcNow);
        next.SetWorkspace(previous.WorkspaceJson);
        next.Publish(Guid.NewGuid(), DateTime.UtcNow);
        _db.WorkflowVersions.Add(next);await _db.SaveChangesAsync();
        var workspace = Workspace(NWFM.Shared.Organization.OrgScopeSet.Unrestricted());
        (await workspace.ChildrenAsync(default)).Select(c=>c.VersionId).Should().Contain(next.Id).And.NotContain(previous.Id);
        (await workspace.ChildrenAsync(default,previous.Id)).Select(c=>c.VersionId).Should().Contain([next.Id,previous.Id]);
    }
}
