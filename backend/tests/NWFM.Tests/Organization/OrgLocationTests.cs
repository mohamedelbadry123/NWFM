namespace NWFM.Tests.Organization;

using FluentAssertions;
using NWFM.Shared.Organization;

/// <summary>
/// The placement rule behind <c>IOrgDirectory.IsValidLocationAsync</c>: a location may only name units
/// that are active and sit under the unit named above them. Branch and operation area are siblings
/// under the CBU — never a chain — so each is checked against the CBU alone.
/// </summary>
public sealed class OrgLocationTests
{
    /// <summary>
    /// Active units only. Cluster C1 holds CBU CB1 (branches BR1, BR2; area OA1) and CBU CB2 (branch BR3).
    /// Cluster C2 holds CBU CB3 (branch BR4). Cluster C3 is inactive; CB9 sits under it.
    /// </summary>
    private static readonly OrgHierarchy Active = OrgHierarchy.Build(
        [("CB1", "C1"), ("CB2", "C1"), ("CB3", "C2"), ("CB9", "C3")],
        [("BR1", "CB1"), ("BR2", "CB1"), ("BR3", "CB2"), ("BR4", "CB3")],
        [("OA1", "CB1")]);

    private static readonly string[] ActiveClusters = ["C1", "C2"];

    private static bool Fits(OrgLocation location) => location.FitsHierarchy(Active, ActiveClusters);

    [Theory]
    [InlineData("C1", "CB1", null, null)]
    [InlineData("C1", "CB1", "BR1", null)]
    [InlineData("C1", "CB1", null, "OA1")]
    [InlineData("C1", "CB1", "BR2", "OA1")]
    [InlineData("C2", "CB3", "BR4", null)]
    [InlineData(null, "CB2", "BR3", null)]
    [InlineData("C1", null, null, null)]
    [InlineData(" c1 ", "cb1", "br1", null)]
    public void AcceptsUnitsThatSitUnderTheirParents(string? cluster, string? cbu, string? branch, string? area) =>
        Fits(new(cluster, cbu, branch, area)).Should().BeTrue();

    [Fact]
    public void RejectsACbuUnderAnotherCluster() =>
        Fits(new("C2", "CB1")).Should().BeFalse();

    [Fact]
    public void RejectsABranchUnderAnotherCbu() =>
        Fits(new("C1", "CB1", "BR3")).Should().BeFalse();

    [Fact]
    public void RejectsAnOperationAreaUnderAnotherCbu() =>
        Fits(new("C1", "CB2", null, "OA1")).Should().BeFalse();

    [Fact]
    public void BranchAndOperationAreaAreSiblings_SoOneIsNeverAcceptedAsTheOther()
    {
        Fits(new("C1", "CB1", "OA1")).Should().BeFalse("an operation area code is not a branch");
        Fits(new("C1", "CB1", null, "BR1")).Should().BeFalse("a branch code is not an operation area");
    }

    [Theory]
    [InlineData(null, null, "BR1", null)]
    [InlineData("C1", null, null, "OA1")]
    public void RejectsABranchOrAreaNamedWithoutItsCbu(string? cluster, string? cbu, string? branch, string? area) =>
        Fits(new(cluster, cbu, branch, area)).Should().BeFalse();

    [Fact]
    public void RejectsUnknownOrInactiveUnits()
    {
        Fits(new("C3")).Should().BeFalse("the cluster is inactive");
        Fits(new(null, "CB9")).Should().BeFalse("its cluster is inactive");
        Fits(new("C1", "CB404")).Should().BeFalse("the CBU is unknown or inactive");
        Fits(new("C1", "CB1", "BR404")).Should().BeFalse("the branch is unknown or inactive");
    }

    [Fact]
    public void IsCoveredBy_AppliesTheSharedScopeRule()
    {
        var hierarchy = OrgHierarchy.Build([("CB1", "C1"), ("CB3", "C2")], [("BR1", "CB1")], [("OA1", "CB1")]);
        var cbuScope = OrgScopeSet.FromRows([new(OrgLevels.Cbu, "CB1", null)], hierarchy);
        var branchScope = OrgScopeSet.FromRows([new(OrgLevels.Branch, "BR1", null)], hierarchy);

        new OrgLocation("C1", "CB1").IsCoveredBy(cbuScope).Should().BeTrue();
        new OrgLocation("C1", "CB1", "BR1").IsCoveredBy(branchScope).Should().BeTrue();
        new OrgLocation("C1", "CB1").IsCoveredBy(branchScope).Should().BeFalse("a branch scope reaches only branch-stamped work");
        new OrgLocation("C2", "CB3").IsCoveredBy(cbuScope).Should().BeFalse();
        OrgLocation.Empty.IsCoveredBy(cbuScope).Should().BeFalse("unplaced work is never assumed to be anyone's");
        OrgLocation.Empty.IsCoveredBy(OrgScopeSet.Unrestricted()).Should().BeTrue();
    }
}
