namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using Moq;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Services;

/// <summary>
/// A workflow's organization location on the shared hierarchy: how stored settings are read (including
/// the pre-hierarchy shape), which placements may be saved, and which instances a caller's coverage reaches.
/// </summary>
public sealed class WorkflowOrgLocationTests
{
    /// <summary>Cluster C1 holds CBU CB1 (branches BR1, BR2; area OA1) and CBU CB2 (branch BR3). Cluster C2 holds CB3 (BR4).</summary>
    private static readonly OrgHierarchy Hierarchy = OrgHierarchy.Build(
        [("CB1", "C1"), ("CB2", "C1"), ("CB3", "C2")],
        [("BR1", "CB1"), ("BR2", "CB1"), ("BR3", "CB2"), ("BR4", "CB3")],
        [("OA1", "CB1")]);

    private static OrgScopeSet Scope(params OrgScopeRow[] rows) => OrgScopeSet.FromRows(rows, Hierarchy);

    // ── Reading stored settings ────────────────────────────────────────────

    [Fact]
    public void Read_CanonicalSettings()
    {
        var settings = WorkflowWorkspaceDefinition.Read(
            """{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","branchCode":"BR1","operationAreaCode":"OA1","designerVersion":2}""")!;

        settings.Kind.Should().Be("Main");
        settings.Location.Should().Be(new OrgLocation("C1", "CB1", "BR1", "OA1"));
        settings.DesignerVersion.Should().Be(2);
        settings.HasLegacyConflict.Should().BeFalse();
        settings.HasRequiredLocation.Should().BeTrue();
    }

    [Fact]
    public void Read_LegacyRegionAndCity_AreTheCbuAndBranchTheyWereChosenFrom()
    {
        // Exactly what published versions in existing databases hold.
        var settings = WorkflowWorkspaceDefinition.Read("""{"kind":"Main","clusterCode":"CC","regionCode":"RCBU","cityCode":"1110","designerVersion":2}""")!;

        settings.Location.Should().Be(new OrgLocation("CC", "RCBU", "1110"));
        settings.HasLegacyConflict.Should().BeFalse();
    }

    [Fact]
    public void Read_LegacyChildWithNullGeography_HasNoLocation()
    {
        var settings = WorkflowWorkspaceDefinition.Read("""{"kind":"Child","clusterCode":null,"regionCode":null,"cityCode":null}""")!;

        settings.Kind.Should().Be("Child");
        settings.Location.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Read_BothShapesAgreeing_IsNotAConflict() =>
        WorkflowWorkspaceDefinition.Read("""{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","regionCode":"cb1"}""")!
            .HasLegacyConflict.Should().BeFalse();

    [Theory]
    [InlineData("""{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","regionCode":"CB2"}""")]
    [InlineData("""{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","branchCode":"BR1","cityCode":"BR2"}""")]
    public void Read_BothShapesDisagreeing_IsFlaggedNotResolved(string json) =>
        WorkflowWorkspaceDefinition.Read(json)!.HasLegacyConflict.Should().BeTrue();

    [Fact]
    public void Read_ToleratesCasingAndBlankSettings()
    {
        WorkflowWorkspaceDefinition.Read("""{"Kind":"Main","ClusterCode":"C1","RegionCode":"CB1"}""")!.Location.CbuCode.Should().Be("CB1");
        WorkflowWorkspaceDefinition.Read(null).Should().BeNull();
        WorkflowWorkspaceDefinition.Read("null").Should().BeNull();
        FluentActions.Invoking(() => WorkflowWorkspaceDefinition.Read("[]")).Should().Throw<JsonException>();
        WorkflowWorkspaceDefinition.KindOf("not json").Should().BeNull();
    }

    [Fact]
    public void Serialize_WritesOnlyTheCanonicalShape()
    {
        var json = JsonSerializer.Serialize(new WorkflowWorkspaceDefinition("Main", "C1", "CB1", "BR1"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"cbuCode\":\"CB1\"").And.Contain("\"branchCode\":\"BR1\"");
        json.Should().NotContain("regionCode").And.NotContain("cityCode").And.NotContain("location").And.NotContain("hasLegacyConflict");
    }

    // ── Validation shared by create, publish and start ─────────────────────

    private static async Task<IReadOnlyList<string>> Issues(string json, bool directorySaysValid = true)
    {
        var directory = new Mock<IOrgDirectory>();
        directory.Setup(d => d.IsValidLocationAsync(It.IsAny<OrgLocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(directorySaysValid);
        var issues = await WorkflowWorkspacePublisher.ValidateLocationAsync(WorkflowWorkspaceDefinition.Read(json)!, directory.Object, default);
        return issues.Select(i => i.Code).ToList();
    }

    [Fact]
    public async Task Validation_AcceptsAValidMainLocation() =>
        (await Issues("""{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","operationAreaCode":"OA1"}""")).Should().BeEmpty();

    [Fact]
    public async Task Validation_ChecksTheLegacyShapeAgainstTheSharedDirectoryAsCbuAndBranch()
    {
        var directory = new Mock<IOrgDirectory>();
        directory.Setup(d => d.IsValidLocationAsync(It.IsAny<OrgLocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await WorkflowWorkspacePublisher.ValidateLocationAsync(
            WorkflowWorkspaceDefinition.Read("""{"kind":"Main","clusterCode":"CC","regionCode":"RCBU","cityCode":"1110"}""")!, directory.Object, default);

        directory.Verify(d => d.IsValidLocationAsync(new OrgLocation("CC", "RCBU", "1110", null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("""{"kind":"Main"}""")]
    [InlineData("""{"kind":"Main","clusterCode":"C1"}""")]
    [InlineData("""{"kind":"Main","cbuCode":"CB1"}""")]
    public async Task Validation_MainNeedsAtLeastClusterAndCbu(string json) =>
        (await Issues(json)).Should().Equal("WORKSPACE_LOCATION_REQUIRED");

    [Fact]
    public async Task Validation_RejectsWhatTheDirectoryRejects() =>
        (await Issues("""{"kind":"Main","clusterCode":"C2","cbuCode":"CB1"}""", directorySaysValid: false)).Should().Equal("WORKSPACE_LOCATION");

    [Fact]
    public async Task Validation_ChildMayNotNameAPlace()
    {
        (await Issues("""{"kind":"Child"}""")).Should().BeEmpty();
        (await Issues("""{"kind":"Child","cbuCode":"CB1"}""")).Should().Equal("WORKSPACE_INHERITANCE");
        (await Issues("""{"kind":"Child","regionCode":"CB1"}""")).Should().Equal("WORKSPACE_INHERITANCE");
    }

    [Fact]
    public async Task Validation_ALegacyConflictBlocksBeforeAnythingIsGuessed() =>
        (await Issues("""{"kind":"Main","clusterCode":"C1","cbuCode":"CB1","regionCode":"CB2"}""")).Should().Equal("WORKSPACE_LOCATION_CONFLICT");

    // ── Instances and coverage ─────────────────────────────────────────────

    private static WorkflowInstance Instance(OrgLocation? location)
    {
        var instance = WorkflowInstance.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), "entity", "start", DateTime.UtcNow);
        instance.SetExecutionContext(location, false);
        return instance;
    }

    private static readonly WorkflowInstance AtCb1 = Instance(new("C1", "CB1"));
    private static readonly WorkflowInstance AtBr1 = Instance(new("C1", "CB1", "BR1"));
    private static readonly WorkflowInstance AtOa1 = Instance(new("C1", "CB1", null, "OA1"));
    private static readonly WorkflowInstance AtCb3 = Instance(new("C2", "CB3", "BR4"));
    private static readonly WorkflowInstance Unplaced = Instance(null);
    private static readonly WorkflowInstance[] All = [AtCb1, AtBr1, AtOa1, AtCb3, Unplaced];

    private static IEnumerable<WorkflowInstance> Visible(OrgScopeSet scope) => All.AsQueryable().Where(WorkflowScopeFilter.ForScope(scope)).ToList();

    [Fact]
    public void Coverage_UnrestrictedSeesEverything() =>
        Visible(OrgScopeSet.Unrestricted()).Should().BeEquivalentTo(All);

    [Fact]
    public void Coverage_AClusterScopeExpandsThroughItsCbus() =>
        Visible(Scope(new OrgScopeRow(OrgLevels.Cluster, "C1", null))).Should().BeEquivalentTo([AtCb1, AtBr1, AtOa1]);

    [Fact]
    public void Coverage_ABranchScopeReachesOnlyBranchStampedWork() =>
        Visible(Scope(new OrgScopeRow(OrgLevels.Branch, "BR1", null))).Should().BeEquivalentTo([AtBr1]);

    [Fact]
    public void Coverage_AnOperationAreaScopeDoesNotReachItsSiblingBranches() =>
        Visible(Scope(new OrgScopeRow(OrgLevels.OperationArea, "OA1", null))).Should().BeEquivalentTo([AtOa1]);

    [Fact]
    public void Coverage_UnplacedInstancesAreNeverAssumedToBeEveryones() =>
        Visible(Scope(new OrgScopeRow(OrgLevels.Cluster, "C1", null), new(OrgLevels.Cluster, "C2", null))).Should().NotContain(Unplaced);

    [Fact]
    public void Coverage_ADepartmentOnlyRowReachesAllTerritory() =>
        Visible(Scope(new OrgScopeRow(null, null, "10"))).Should().BeEquivalentTo(All);

    [Fact]
    public void Coverage_DepartmentGroupsUnionForAnInstance_ButActingNeedsTheActivitysDepartmentInTheSameGroup()
    {
        // Water (10) in CB1 and waste-water (11) in CB3 must not become water in CB3.
        var scope = Scope(new OrgScopeRow(OrgLevels.Cbu, "CB1", "10"), new(OrgLevels.Cbu, "CB3", "11"));

        Visible(scope).Should().BeEquivalentTo([AtCb1, AtBr1, AtOa1, AtCb3]);
        WorkflowScopeFilter.Allows(AtCb3, scope, "11").Should().BeTrue();
        WorkflowScopeFilter.Allows(AtCb3, scope, "10").Should().BeFalse();
        WorkflowScopeFilter.Allows(AtCb1, scope, "10").Should().BeTrue();
    }

    [Fact]
    public void Instance_StoresTheLocationTrimmed_AndRejectsOversizedCodes()
    {
        Instance(new(" C1 ", "CB1", " ", null)).Location.Should().Be(new OrgLocation("C1", "CB1"));
        FluentActions.Invoking(() => Instance(new("C1", new string('X', 51)))).Should().Throw<DomainException>();
    }

    [Fact]
    public void EventVariables_UseCanonicalNames_AndKeepTheLegacyAliases()
    {
        var variables = WorkflowActivityEvents.LocationVariables(Instance(new("C1", "CB1", "BR1", "OA1")));

        variables.Should().Contain(new KeyValuePair<string, object?>("ClusterCode", "C1"))
            .And.Contain(new KeyValuePair<string, object?>("CbuCode", "CB1"))
            .And.Contain(new KeyValuePair<string, object?>("BranchCode", "BR1"))
            .And.Contain(new KeyValuePair<string, object?>("OperationAreaCode", "OA1"))
            .And.Contain(new KeyValuePair<string, object?>("RegionCode", "CB1"))
            .And.Contain(new KeyValuePair<string, object?>("CityCode", "BR1"));
        WorkflowActivityEvents.LocationVariables(Unplaced).Should().BeEmpty();
    }
}
