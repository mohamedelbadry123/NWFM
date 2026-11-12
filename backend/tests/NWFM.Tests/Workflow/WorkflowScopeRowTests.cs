namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.SaveWorkflowDraftXml;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;
using Moq;
using NWFM.Shared.Results;

/// <summary>Scope rows only describe how the canonical scopes were authored; they may never change what is covered.</summary>
public sealed class WorkflowScopeRowTests
{
    private static WorkflowScopeRow Row(string id, string cluster, string? cbu = null, string? branch = null, string? area = null) => new(id, cluster, cbu, branch, area);

    private static WorkflowWorkspaceDefinition Workspace(IReadOnlyList<WorkflowScopeRow>? rows, params WorkflowOrganizationScope[] scopes) =>
        new("Main") { SchemaVersion = 2, OrganizationScopes = scopes, ScopeRows = rows };

    [Fact]
    public void Expand_KeepsBranchAndAreaAsAUnionNotAnIntersection()
    {
        Row("a", "A", "A1", "AB1", "AA1").Expand().Select(s => (s.Level, s.Code)).Should().Equal(("Branch", "AB1"), ("OperationArea", "AA1"));
        Row("b", "A", "A1").Expand().Should().ContainSingle().Which.Level.Should().Be("Cbu");
        Row("c", "A").Expand().Should().ContainSingle().Which.Should().Be(new WorkflowOrganizationScope("Cluster", "A", "A"));
    }

    [Fact]
    public void Validate_AcceptsOlderSavesWithoutRows()
    {
        WorkflowScopeRows.Validate(Workspace(null, new WorkflowOrganizationScope("Cluster", "A", "A"))).Should().BeNull();
        WorkflowScopeRows.Validate(null).Should().BeNull();
    }

    [Fact]
    public void Validate_AcceptsRowsThatExpandToTheCanonicalScopes_InAnyOrder()
    {
        WorkflowScopeRow[] rows = [Row("r1", "A", "A1", "AB1", "AA1"), Row("r2", "B")];
        WorkflowOrganizationScope[] scopes = [new("Cluster", "B", "B"), new("OperationArea", "AA1", "A", "A1"), new("Branch", "AB1", "A", "A1")];

        WorkflowScopeRows.Validate(Workspace(rows, scopes)).Should().BeNull();
    }

    [Fact]
    public void Validate_KeepsOverlappingBroaderAndNarrowerEntriesInsteadOfCollapsingThem()
    {
        var rows = new[] { Row("whole", "A"), Row("part", "A", "A1") };
        WorkflowOrganizationScope[] both = [new("Cluster", "A", "A"), new("Cbu", "A1", "A", "A1")];

        WorkflowScopeRows.Validate(Workspace(rows, both)).Should().BeNull();
        // Dropping the narrower scope from the canonical list would silently lose an authored entry.
        WorkflowScopeRows.Validate(Workspace(rows, new WorkflowOrganizationScope("Cluster", "A", "A"))).Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsRowsThatWidenOrNarrowTheCanonicalScopes()
    {
        var rows = new[] { Row("r1", "A", "A1") };

        WorkflowScopeRows.Validate(Workspace(rows, new WorkflowOrganizationScope("Cluster", "A", "A"))).Should().NotBeNull();
        WorkflowScopeRows.Validate(Workspace(rows, new WorkflowOrganizationScope("Cbu", "A1", "A", "A1"), new WorkflowOrganizationScope("Cbu", "A2", "A", "A2"))).Should().NotBeNull();
        WorkflowScopeRows.Validate(Workspace(rows)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("", "A", null, null, null)]
    [InlineData("r1", "", null, null, null)]
    [InlineData("r1", "A", null, "AB1", null)]
    [InlineData("r1", "A", null, null, "AA1")]
    [InlineData("r1", " A", null, null, null)]
    public void Validate_RejectsMalformedRows(string id, string cluster, string? cbu, string? branch, string? area) =>
        WorkflowScopeRows.Validate(Workspace([Row(id, cluster, cbu, branch, area)], new WorkflowOrganizationScope("Cluster", "A", "A"))).Should().NotBeNull();

    [Fact]
    public void Validate_RejectsDuplicateEntriesAndIdentifiers()
    {
        WorkflowOrganizationScope[] scopes = [new("Cluster", "A", "A")];

        WorkflowScopeRows.Validate(Workspace([Row("r1", "A"), Row("r2", "a")], scopes)).Should().Contain("twice");
        WorkflowScopeRows.Validate(Workspace([Row("r1", "A"), Row("r1", "A", "A1")], scopes)).Should().Contain("identifier");
    }

    [Fact]
    public void Read_RoundTripsRowsAndLeavesOlderSettingsReadable()
    {
        var json = """{"kind":"Main","schemaVersion":2,"organizationScopes":[{"level":"Cluster","code":"A","clusterCode":"A"}],"scopeRows":[{"id":"r1","clusterCode":"A"}]}""";
        var read = WorkflowWorkspaceDefinition.Read(json)!;

        read.ScopeRows.Should().Equal(Row("r1", "A"));
        WorkflowScopeRows.Validate(read).Should().BeNull();
        JsonSerializer.Serialize(read, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().Contain("\"scopeRows\"");

        var legacy = WorkflowWorkspaceDefinition.Read("""{"kind":"Main","schemaVersion":2,"organizationScopes":[{"level":"Cluster","code":"A","clusterCode":"A"}]}""")!;
        legacy.ScopeRows.Should().BeNull();
        legacy.HasRequiredLocation.Should().BeTrue();
    }

    private static async Task<(Result<global::Workflow.Application.DTOs.WorkflowVersionDto> Result, WorkflowVersion Version)> SaveDraft(string workspaceJson)
    {
        var xml = "<Workflow xmlns=\"https://privora.io/workflow/v1\" workspaceJson=\"" + System.Security.SecurityElement.Escape(workspaceJson) + "\"><Activities><Activity nodeKey=\"start\" type=\"Start\" name=\"Start\"/><Activity nodeKey=\"end\" type=\"End\" name=\"End\"/></Activities><Transitions><Transition key=\"finish\" from=\"start\" to=\"end\"/></Transitions></Workflow>";
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        var gate = new Mock<IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var repository = new Mock<IWorkflowVersionRepository>();
        repository.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var handler = new SaveWorkflowDraftXmlCommandHandler(gate.Object, repository.Object, new WorkflowXmlCompiler());
        return (await handler.Handle(new(version.Id, xml, "{}", ""), default), version);
    }

    [Fact]
    public async Task SavingADraft_RefusesEntriesThatDisagreeWithTheScopes_AndAcceptsConsistentOrOlderSettings()
    {
        const string scopes = """[{"level":"Cluster","code":"A","clusterCode":"A"}]""";
        var inconsistent = await SaveDraft("""{"kind":"Main","schemaVersion":2,"organizationScopes":""" + scopes + ""","scopeRows":[{"id":"r1","clusterCode":"B"}]}""");
        inconsistent.Result.IsFailure.Should().BeTrue();
        inconsistent.Result.Error.Code.Should().Be(WorkflowScopeRows.InvalidCode);
        inconsistent.Version.WorkspaceJson.Should().BeNull("a refused save must not change the draft");

        var consistent = await SaveDraft("""{"kind":"Main","schemaVersion":2,"organizationScopes":""" + scopes + ""","scopeRows":[{"id":"r1","clusterCode":"A"}]}""");
        consistent.Result.IsSuccess.Should().BeTrue(consistent.Result.IsFailure ? consistent.Result.Error.Message : "");
        consistent.Version.WorkspaceJson.Should().Contain("scopeRows");

        var older = await SaveDraft("""{"kind":"Main","schemaVersion":2,"organizationScopes":""" + scopes + "}");
        older.Result.IsSuccess.Should().BeTrue(older.Result.IsFailure ? older.Result.Error.Message : "");
    }
}
