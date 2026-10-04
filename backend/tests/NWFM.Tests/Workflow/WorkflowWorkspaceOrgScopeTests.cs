namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;

/// <summary>
/// The workspace end to end on the shared org hierarchy: a workflow published with the pre-hierarchy
/// settings still starts and stamps its tree, and every entry point narrows work to the caller's coverage.
/// </summary>
public sealed partial class WorkflowRuntimeEnginePathTests
{
    /// <summary>Cluster CC holds CBU R1 (branch C1) — the seeded workflow's place — and CBU R2 (branch C2).</summary>
    private static readonly OrgHierarchy WorkspaceHierarchy = OrgHierarchy.Build([("R1", "CC"), ("R2", "CC")], [("C1", "R1"), ("C2", "R2")], []);

    private static OrgScopeSet CoverageOf(params OrgScopeRow[] rows) => OrgScopeSet.FromRows(rows, WorkspaceHierarchy);

    private WorkflowWorkspace Workspace(OrgScopeSet scope, bool locationIsValid = true)
    {
        var scopes = new Mock<IOrgScopeProvider>();
        scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        var directory = new Mock<IOrgDirectory>();
        directory.Setup(d => d.IsValidLocationAsync(It.IsAny<OrgLocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(locationIsValid);
        directory.Setup(d => d.GetUnitNamesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OrgUnitName>());
        var groups = new Mock<IWorkflowGroupDirectory>();
        groups.Setup(g => g.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new WorkflowWorkspace(_db, new StubTenant(_orgId), null!, BuildEngine(_db), groups.Object, null!,
            new WorkflowVersionRepository(_db), new WorkflowActivityEvents(_db, Integrations()), scopes.Object, directory.Object);
    }

    private async Task<Guid> SeededDefinitionAsync(Guid version) => (await _db.WorkflowVersions.FindAsync(version))!.WorkflowDefinitionId;

    [Fact]
    public async Task Start_LegacyPublishedWorkflow_StampsRootAndChildWithItsCbuAndBranch()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);

        var started = await Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cbu, "R1", null)))
            .StartAsync(await SeededDefinitionAsync(seeded.Version), new(Guid.NewGuid(), "REF-1"), Guid.NewGuid(), false, default);

        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var root = await _db.WorkflowInstances.SingleAsync(i => i.Id == started.Value);
        root.Location.Should().Be(new OrgLocation("CC", "R1", "C1"));
        (await _db.WorkflowInstances.SingleAsync(i => i.ParentInstanceId == root.Id)).Location.Should().Be(root.Location);
    }

    [Fact]
    public async Task Start_RefusesAWorkflowOutsideTheCallersCoverage()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        var workspace = Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cbu, "R2", null)));

        (await workspace.CatalogAsync(default)).Should().BeEmpty();
        var started = await workspace.StartAsync(await SeededDefinitionAsync(seeded.Version), new(Guid.NewGuid(), null), Guid.NewGuid(), false, default);

        started.Error.Message.Should().Contain("outside your organization coverage");
        (await _db.WorkflowInstances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Start_RefusesALocationTheDirectoryNoLongerAccepts()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);

        var started = await Workspace(OrgScopeSet.Unrestricted(), locationIsValid: false)
            .StartAsync(await SeededDefinitionAsync(seeded.Version), new(Guid.NewGuid(), null), Guid.NewGuid(), false, default);

        started.Error.Message.Should().Contain("no longer valid");
    }

    [Fact]
    public async Task Catalog_ListsWorkflowsWithinCoverage()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);

        (await Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cluster, "CC", null))).CatalogAsync(default))
            .Select(w => w.VersionId).Should().Equal(seeded.Version);
        (await Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Branch, "C1", null))).CatalogAsync(default))
            .Select(w => w.VersionId).Should().Equal(seeded.Version);
    }

    [Fact]
    public async Task ListAndDetail_ShowOnlyInstancesWithinCoverage_AndNeverUnplacedOnesToRestrictedCallers()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var inR1 = (await BuildEngine(_db).StartAsync(_orgId, seeded.Binding.Id, "case", "in-r1", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        WorkflowInstance Root(string key, OrgLocation? location)
        {
            var instance = WorkflowInstance.Start(_orgId, seeded.Binding.Id, seeded.Version, key, key, "start", now);
            instance.SetExecutionContext(location, false);
            _db.WorkflowInstances.Add(instance);
            return instance;
        }
        var inR2 = Root("in-r2", new("CC", "R2", "C2"));
        var unplaced = Root("unplaced", null);
        await _db.SaveChangesAsync();

        var restricted = Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cbu, "R1", null)));
        (await restricted.ListAsync(null, default)).Select(i => i.Id).Should().Equal(inR1.Id);
        (await restricted.GetAsync(inR2.Id, Guid.NewGuid(), false, null, default)).Error.Message.Should().Be("Instance not found.");
        (await restricted.GetAsync(unplaced.Id, Guid.NewGuid(), false, null, default)).Error.Message.Should().Be("Instance not found.");

        var summaries = await Workspace(OrgScopeSet.Unrestricted()).ListAsync(null, default);
        summaries.Select(i => i.Id).Should().BeEquivalentTo([inR1.Id, inR2.Id, unplaced.Id]);
        summaries.Single(i => i.Id == inR1.Id).Location.Should().Be(new OrgLocation("CC", "R1", "C1"));
    }

    [Fact]
    public async Task Act_RefusesTasksOutsideCoverage_IncludingTheActivitysDepartment()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        var main = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == seeded.Version && a.ActivityType == ActivityType.MainActivity);
        var config = JsonNode.Parse(main.ConfigurationJson!)!;
        config["departmentCode"] = "11";
        _db.Entry(main).Property(a => a.ConfigurationJson).CurrentValue = config.ToJsonString();
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db);
        var root = (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "act", now, pinnedWorkflowVersionId: seeded.Version)).Value;
        await engine.ResumeFromTimerAsync((await _db.WorkflowTimers.SingleAsync()).Id, now.AddMinutes(1));
        await engine.ResumeFromCallActivityAsync(root.Id, "activity", now.AddMinutes(1));
        var task = await _db.WorkItems.SingleAsync();

        var elsewhere = await Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cbu, "R2", null)))
            .ActAsync(task.Id, new(Guid.NewGuid(), "APPROVE"), Guid.NewGuid(), false, default);
        elsewhere.Error.Message.Should().Be("Task not found.");

        var otherDepartment = await Workspace(CoverageOf(new OrgScopeRow(OrgLevels.Cbu, "R1", "10")))
            .ActAsync(task.Id, new(Guid.NewGuid(), "APPROVE"), Guid.NewGuid(), false, default);
        otherDepartment.Error.Message.Should().Contain("department or activity type is outside your organization coverage");
    }

    [Fact]
    public async Task Create_RejectsAnIncompleteOrInvalidMainLocation()
    {
        var incomplete = await Workspace(OrgScopeSet.Unrestricted())
            .CreateAsync(new("Leak repair", null, new("Main", "CC")), Guid.NewGuid(), default);
        incomplete.Error.Message.Should().Contain("cluster and a CBU");

        var invalid = await Workspace(OrgScopeSet.Unrestricted(), locationIsValid: false)
            .CreateAsync(new("Leak repair", null, new("Main", "CC", "R1", "C2")), Guid.NewGuid(), default);
        invalid.Error.Message.Should().Contain("must belong to that CBU");
    }

    [Fact]
    public async Task Engine_StampsTheCanonicalLocationIncludingOperationArea()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        (await _db.WorkflowVersions.FindAsync(seeded.Version))!
            .SetWorkspace("""{"kind":"Main","clusterCode":"CC","cbuCode":"R1","branchCode":"C1","operationAreaCode":"OA7","designerVersion":2}""");
        await _db.SaveChangesAsync();

        var root = await BuildEngine(_db).StartAsync(_orgId, seeded.Binding.Id, "case", "canonical", now, pinnedWorkflowVersionId: seeded.Version);

        root.Value.Location.Should().Be(new OrgLocation("CC", "R1", "C1", "OA7"));
        (await _db.WorkflowInstances.SingleAsync(i => i.ParentInstanceId == root.Value.Id)).Location.Should().Be(root.Value.Location);
    }

    [Fact]
    public async Task Engine_RefusesToStartAConflictingLegacyLocation()
    {
        var now = DateTime.UtcNow;
        var seeded = SeedWorkspace(now);
        (await _db.WorkflowVersions.FindAsync(seeded.Version))!
            .SetWorkspace("""{"kind":"Main","clusterCode":"CC","cbuCode":"R1","regionCode":"R2"}""");
        await _db.SaveChangesAsync();

        var root = await BuildEngine(_db).StartAsync(_orgId, seeded.Binding.Id, "case", "conflict", now, pinnedWorkflowVersionId: seeded.Version);

        root.IsFailure.Should().BeTrue();
        root.Error.Code.Should().Be("Workflow.Location.Invalid");
    }
}
