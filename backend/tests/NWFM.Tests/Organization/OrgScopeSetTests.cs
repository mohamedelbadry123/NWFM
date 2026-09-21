namespace NWFM.Tests.Organization;

using FluentAssertions;
using NWFM.Shared.Organization;

/// <summary>
/// The territory rule every task list and task command is judged by. A loose rule here shows one
/// crew another crew's work, so the edges are pinned down.
/// </summary>
public sealed class OrgScopeSetTests
{
    /// <summary>
    /// Cluster C1 holds CBU CB1 (branches BR1, BR2; area OA1) and CBU CB2 (branch BR3).
    /// Cluster C2 holds CBU CB3 (branch BR4).
    /// </summary>
    private static readonly OrgHierarchy Hierarchy = OrgHierarchy.Build(
        [("CB1", "C1"), ("CB2", "C1"), ("CB3", "C2")],
        [("BR1", "CB1"), ("BR2", "CB1"), ("BR3", "CB2"), ("BR4", "CB3")],
        [("OA1", "CB1")]);

    private static OrgScopeSet Scope(params OrgScopeRow[] rows) => OrgScopeSet.FromRows(rows, Hierarchy);

    [Fact]
    public void NoRowsAtAll_IsUnrestricted()
    {
        var scope = Scope();

        scope.IsUnrestricted.Should().BeTrue();
        scope.Covers("CB3", "BR4", null, "50").Should().BeTrue();
    }

    [Fact]
    public void AClusterScope_ReachesEveryCbuBranchAndAreaBeneathIt()
    {
        var scope = Scope(new OrgScopeRow(OrgLevels.Cluster, "C1", null));

        scope.Covers(null, "BR3", null, null).Should().BeTrue();
        scope.Covers(null, null, "OA1", null).Should().BeTrue();
        scope.Covers("CB1", null, null, null).Should().BeTrue();
        scope.Covers(null, "BR4", null, null).Should().BeFalse();
    }

    [Fact]
    public void ABranchScope_DoesNotReachItsSiblingOperationAreas()
    {
        // Branch and operation area are siblings under the CBU, not parent and child.
        var scope = Scope(new OrgScopeRow(OrgLevels.Branch, "BR1", null));

        scope.Covers(null, "BR1", null, null).Should().BeTrue();
        scope.Covers(null, null, "OA1", null).Should().BeFalse();
        scope.Covers("CB1", null, null, null).Should().BeFalse();
    }

    [Fact]
    public void DepartmentsAndTerritoriesArePaired_NotCrossed()
    {
        // Water in CB1, waste-water in CB3. Not water in CB3.
        var scope = Scope(
            new OrgScopeRow(OrgLevels.Cbu, "CB1", "10"),
            new OrgScopeRow(OrgLevels.Cbu, "CB3", "11"));

        scope.Covers("CB1", null, null, "10").Should().BeTrue();
        scope.Covers("CB3", null, null, "11").Should().BeTrue();
        scope.Covers("CB3", null, null, "10").Should().BeFalse();
        scope.Covers("CB1", null, null, "11").Should().BeFalse();
    }

    [Fact]
    public void WorkWithNoDepartment_IsAdmittedByAnyGroup()
    {
        var scope = Scope(new OrgScopeRow(OrgLevels.Cbu, "CB1", "10"));

        scope.Covers("CB1", null, null, null).Should().BeTrue();
    }

    [Fact]
    public void ADepartmentOnlyRow_CoversThatDepartmentEverywhere()
    {
        var scope = Scope(new OrgScopeRow(null, null, "10"));

        scope.Covers("CB3", "BR4", null, "10").Should().BeTrue();
        scope.Covers("CB3", "BR4", null, "11").Should().BeFalse();
    }

    [Fact]
    public void CodesMatchWhateverTheirCase()
    {
        var scope = Scope(new OrgScopeRow(OrgLevels.Cbu, "cb1", "10"));

        scope.Covers(null, "br2", null, "10").Should().BeTrue();
    }

    [Fact]
    public void Overlaps_WhenTwoScopesShareAnyTerritoryInTheSameDepartment()
    {
        var crew = Scope(new OrgScopeRow(OrgLevels.Branch, "BR1", "10"));
        var supervisor = Scope(new OrgScopeRow(OrgLevels.Cluster, "C1", "10"));
        var elsewhere = Scope(new OrgScopeRow(OrgLevels.Cluster, "C2", "10"));

        supervisor.Overlaps(crew).Should().BeTrue();
        elsewhere.Overlaps(crew).Should().BeFalse();
    }

    [Fact]
    public void ToTerritories_FlattensEachDepartmentGroupForAQuery()
    {
        var scope = Scope(new OrgScopeRow(OrgLevels.Cbu, "CB1", "10"));

        var territory = scope.ToTerritories().Should().ContainSingle().Subject;

        territory.DepartmentCode.Should().Be("10");
        territory.CbuCodes.Should().BeEquivalentTo(["CB1"]);
        territory.BranchCodes.Should().BeEquivalentTo(["BR1", "BR2"]);
        territory.OperationAreaCodes.Should().BeEquivalentTo(["OA1"]);
    }
}
