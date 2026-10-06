namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Organization;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private static string ScopeJson(string kind, params WorkflowOrganizationScope[] scopes) =>
        JsonSerializer.Serialize(new WorkflowWorkspaceDefinition(kind) { SchemaVersion = 2, OrganizationScopes = scopes }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task ScopedWorkflow_ResolvesSelectedLocationAndStampsChild()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        (await _db.WorkflowVersions.FindAsync(seeded.Version))!.SetWorkspace(ScopeJson("Main", new("Cluster", "CC", "CC"), new("Cluster", "OTHER", "OTHER")));
        (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!.SetWorkspace(ScopeJson("Child", new WorkflowOrganizationScope("Cbu", "R1", "CC", "R1")));
        await _db.SaveChangesAsync();
        var location = new OrgLocation("CC", "R1");
        var engine = BuildEngine(_db);
        var result = await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "scoped", DateTime.UtcNow,
            pinnedWorkflowVersionId: seeded.Version, executionLocation: location);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Location.Should().Be(location);
        (await _db.WorkflowInstances.SingleAsync(i => i.ParentInstanceId == result.Value.Id)).Location.Should().Be(location);
        var duplicate = await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "scoped", DateTime.UtcNow,
            pinnedWorkflowVersionId: seeded.Version, executionLocation: new OrgLocation("OTHER"));
        duplicate.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ScopedWorkflow_RejectsAmbiguousAndChildIncompatibleStartsBeforeCreatingWork()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        (await _db.WorkflowVersions.FindAsync(seeded.Version))!.SetWorkspace(ScopeJson("Main", new("Cluster", "CC", "CC"), new("Cluster", "OTHER", "OTHER")));
        (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!.SetWorkspace(ScopeJson("Child", new WorkflowOrganizationScope("Cbu", "R1", "CC", "R1")));
        await _db.SaveChangesAsync();
        var engine = BuildEngine(_db);
        (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "ambiguous", DateTime.UtcNow, pinnedWorkflowVersionId: seeded.Version)).IsFailure.Should().BeTrue();
        (await engine.StartAsync(_orgId, seeded.Binding.Id, "case", "incompatible", DateTime.UtcNow,
            pinnedWorkflowVersionId: seeded.Version, executionLocation: new OrgLocation("CC", "R2"))).IsFailure.Should().BeTrue();
        (await _db.WorkflowInstances.CountAsync()).Should().Be(0);
        (await _db.WorkItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ScopedWorkflow_ComputesChildIntersection_AndSupportsClusterOnlyExecution()
    {
        var seeded = SeedWorkspace(DateTime.UtcNow);
        (await _db.WorkflowVersions.FindAsync(seeded.Version))!.SetWorkspace(ScopeJson("Main", new WorkflowOrganizationScope("Cluster", "CC", "CC")));
        (await _db.WorkflowVersions.FindAsync(seeded.ChildVersion))!.SetWorkspace(ScopeJson("Child", new WorkflowOrganizationScope("Cluster", "CC", "CC")));
        await _db.SaveChangesAsync();
        var repository = new WorkflowVersionRepository(_db);
        var version = (await repository.GetByIdWithProjectionAsync(seeded.Version))!;
        (await WorkflowScopeRules.StartScopesAsync(version, _db, repository, default)).Should().Equal(new WorkflowOrganizationScope("Cluster", "CC", "CC"));
        var result = await BuildEngine(_db).StartAsync(_orgId, seeded.Binding.Id, "case", "cluster-only", DateTime.UtcNow, pinnedWorkflowVersionId: seeded.Version);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Location.Should().Be(new OrgLocation("CC"));
    }
}
