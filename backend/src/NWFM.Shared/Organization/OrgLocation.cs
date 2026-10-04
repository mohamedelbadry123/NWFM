using System.Text.Json.Serialization;

namespace NWFM.Shared.Organization;

/// <summary>
/// Where a piece of work sits in the org hierarchy — the place attached to it, not anyone's coverage
/// (that is <see cref="OrgScopeSet"/>). Branch and operation area are siblings under the CBU, so both
/// may be named at once and neither implies the other.
/// </summary>
public sealed record OrgLocation(
    string? ClusterCode = null,
    string? CbuCode = null,
    string? BranchCode = null,
    string? OperationAreaCode = null)
{
    public static OrgLocation Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ClusterCode)
        && string.IsNullOrWhiteSpace(CbuCode)
        && string.IsNullOrWhiteSpace(BranchCode)
        && string.IsNullOrWhiteSpace(OperationAreaCode);

    /// <summary>Trimmed codes, with blanks read as "not named".</summary>
    public OrgLocation Normalized() =>
        new(Clean(ClusterCode), Clean(CbuCode), Clean(BranchCode), Clean(OperationAreaCode));

    /// <summary>
    /// Whether <paramref name="scope"/> reaches this place. Work carries no department or activity type
    /// here unless given, so any group in the scope may take it — the reference rule for unclassified work.
    /// </summary>
    public bool IsCoveredBy(OrgScopeSet scope, string? departmentCode = null, string? activityTypeCode = null) =>
        scope.Covers(Clean(CbuCode), Clean(BranchCode), Clean(OperationAreaCode), departmentCode, activityTypeCode);

    /// <summary>
    /// The hierarchy rule, given the units the directory holds as active. <paramref name="active"/> and
    /// <paramref name="activeClusters"/> need only hold the units this location names; a unit missing
    /// from them is unknown or inactive. The CBU must sit under an active cluster (the one named, if
    /// any); the branch and the operation area must each sit under the CBU — neither under the other.
    /// </summary>
    public bool FitsHierarchy(OrgHierarchy active, IReadOnlyCollection<string> activeClusters)
    {
        var location = Normalized();
        bool IsActiveCluster(string code) => activeClusters.Contains(code, StringComparer.OrdinalIgnoreCase);

        if (location.ClusterCode is not null && !IsActiveCluster(location.ClusterCode))
        {
            return false;
        }

        if (location.CbuCode is null)
        {
            // A branch or an operation area is only placed through its CBU.
            return location.BranchCode is null && location.OperationAreaCode is null;
        }

        var cbuCluster = active.ClusterOfCbu(location.CbuCode);
        if (cbuCluster is null || !IsActiveCluster(cbuCluster)
            || (location.ClusterCode is not null && !string.Equals(cbuCluster, location.ClusterCode, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return (location.BranchCode is null
                || active.BranchesUnderCbu(location.CbuCode).Contains(location.BranchCode, StringComparer.OrdinalIgnoreCase))
            && (location.OperationAreaCode is null
                || active.AreasUnderCbu(location.CbuCode).Contains(location.OperationAreaCode, StringComparer.OrdinalIgnoreCase));
    }

    private static string? Clean(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
