namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NWFM.Shared.Organization;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private (WorkflowDefinition Definition, WorkflowVersion Version) LibraryDefinition(string name,
        WorkflowWorkspaceDefinition? settings = null, int number = 1, Guid? organization = null)
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var definition = WorkflowDefinition.Create(organization ?? _orgId, Guid.NewGuid().ToString(), name, now);
        definition.Update(name, null, null, null, now);
        var version = WorkflowVersion.CreateDraft(definition.Id, number, Guid.NewGuid(), now);
        version.SetWorkspace(Config(settings ?? new WorkflowWorkspaceDefinition("Main")));
        _db.WorkflowDefinitions.Add(definition);
        _db.WorkflowVersions.Add(version);
        return (definition, version);
    }

    [Fact]
    public async Task Library_PaginatesWholeFamilies_AndFindsChildBeyondFirstTwoHundred()
    {
        for (var i = 0; i < 205; i++) LibraryDefinition($"Root {i:D3}");
        var parent = LibraryDefinition("Zulu parent");
        var child = LibraryDefinition("Unique meter steps", new("Child") { ParentDefinitionId = parent.Definition.Id });
        await _db.SaveChangesAsync();
        var workspace = Workspace(OrgScopeSet.Unrestricted());
        var page = await workspace.LibraryAsync(new(Page: 21, PageSize: 10, Sort: "Name"), default);
        page.TotalGroups.Should().Be(206);
        page.TotalWorkflows.Should().Be(207);
        page.Items.Should().HaveCount(6);
        page.Items.Last().Children.Should().ContainSingle(i => i.Id == child.Definition.Id);
        var search = await workspace.LibraryAsync(new(Search: "Unique meter"), default);
        search.TotalGroups.Should().Be(1);
        search.TotalWorkflows.Should().Be(1);
        search.Items.Single().Id.Should().Be(parent.Definition.Id);
        search.Items.Single().Children.Single().Id.Should().Be(child.Definition.Id);
    }

    [Fact]
    public async Task Library_UsesLatestVersionContext_AndVersionEditTimestamp()
    {
        var row = LibraryDefinition("Updated workflow");
        row.Version.Publish(Guid.NewGuid(), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        var edited = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var draft = WorkflowVersion.CreateDraft(row.Definition.Id, 2, Guid.NewGuid(), edited.AddDays(-1));
        draft.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Main")
        {
            SchemaVersion = 2,
            OrganizationScopes = [new(OrgLevels.Cluster, "CC", "CC")]
        }));
        draft.UpdateDesignerJson("{}", edited);
        _db.WorkflowVersions.Add(draft);
        await _db.SaveChangesAsync();
        var item = (await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(), default)).Items.Single();
        item.VersionId.Should().Be(draft.Id);
        item.VersionStatus.Should().Be("Draft");
        item.VersionCount.Should().Be(2);
        item.UpdatedAt.Should().Be(edited);
        item.Scopes.Should().ContainSingle(s => s.Code == "CC");
    }

    [Fact]
    public async Task Library_ArchivedParentRemainsAsContextForActiveChild()
    {
        var parent = LibraryDefinition("Archived parent");
        parent.Definition.Deactivate(DateTime.UtcNow);
        var child = LibraryDefinition("Active child", new("Child") { ParentDefinitionId = parent.Definition.Id });
        await _db.SaveChangesAsync();
        var page = await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(), default);
        page.TotalWorkflows.Should().Be(1);
        page.Items.Single().IsActive.Should().BeFalse();
        page.Items.Single().Children.Single().Id.Should().Be(child.Definition.Id);
        var archived = await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(Status: "Archived"), default);
        archived.Items.Single().Children.Should().BeEmpty();
    }

    [Fact]
    public async Task Library_RecoversOrphansCyclesAndMalformedSettings_WithoutCrossTenantLinks()
    {
        var outside = WorkflowDefinition.Create(Guid.NewGuid(), "outside", "Outside tenant", DateTime.UtcNow);
        var options = (DbContextOptions<WorkflowDbContext>)_db.GetService<IDbContextOptions>();
        await using (var otherTenant = new WorkflowDbContext(options, new StubTenant(outside.OrganizationId)))
        {
            otherTenant.WorkflowDefinitions.Add(outside);
            await otherTenant.SaveChangesAsync();
        }
        var orphan = LibraryDefinition("Orphan", new("Child") { ParentDefinitionId = outside.Id });
        var first = LibraryDefinition("Cycle A");
        var second = LibraryDefinition("Cycle B", new("Child") { ParentDefinitionId = first.Definition.Id });
        first.Version.SetWorkspace(Config(new WorkflowWorkspaceDefinition("Child") { ParentDefinitionId = second.Definition.Id }));
        var malformed = LibraryDefinition("Malformed");
        malformed.Version.SetWorkspace("{bad-json");
        await _db.SaveChangesAsync();
        var page = await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(), default);
        var all = page.Items.SelectMany(i => new[] { i }.Concat(i.Children)).ToList();
        all.Should().HaveCount(4).And.NotContain(i => i.Id == outside.Id);
        all.Single(i => i.Id == orphan.Definition.Id).NeedsAttention.Should().BeTrue();
        all.Single(i => i.Id == malformed.Definition.Id).NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Library_InfersLegacyChildFromPinnedParent_AndAdaptsLegacyScope()
    {
        var seed = SeedWorkspace(DateTime.UtcNow);
        var page = await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(), default);
        page.Items.Should().ContainSingle();
        var parent = page.Items.Single();
        parent.Scopes.Should().ContainSingle(s => s.Level == OrgLevels.Branch && s.Code == "C1" && s.CbuCode == "R1");
        parent.Children.Single().VersionId.Should().Be(seed.ChildVersion);
        parent.Children.Single().InheritsScope.Should().BeTrue();
        parent.ActivityCount.Should().Be(1);
    }

    [Fact]
    public async Task Library_ScopeFilterRetainsAncestors_AndClampsPageAfterFiltering()
    {
        var parent = LibraryDefinition("Parent", new("Main") { OrganizationScopes = [new(OrgLevels.Cluster, "A", "A")] });
        var child = LibraryDefinition("Child", new("Child")
        {
            ParentDefinitionId = parent.Definition.Id,
            OrganizationScopes = [new(OrgLevels.Branch, "B1", "B", "B2")]
        });
        LibraryDefinition("Unrelated");
        await _db.SaveChangesAsync();
        var page = await Workspace(OrgScopeSet.Unrestricted()).LibraryAsync(new(Page: 99, Scope: "Branch:B1"), default);
        page.Page.Should().Be(1);
        page.Items.Single().Id.Should().Be(parent.Definition.Id);
        page.Items.Single().Children.Single().Id.Should().Be(child.Definition.Id);
        page.Scopes.Should().HaveCount(2);
    }
}
