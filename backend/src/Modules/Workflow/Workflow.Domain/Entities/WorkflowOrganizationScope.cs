using System.Text.Json.Serialization;
using NWFM.Shared.Organization;

namespace Workflow.Domain.Entities;

/// <summary>One eligible territory. Leaf scopes are alternatives, not a Cartesian product.</summary>
public sealed record WorkflowOrganizationScope(string Level, string Code, string ClusterCode, string? CbuCode = null)
{
    [JsonIgnore]
    public OrgLocation Location => new(ClusterCode, Level == OrgLevels.Cluster ? null : CbuCode,
        Level == OrgLevels.Branch ? Code : null, Level == OrgLevels.OperationArea ? Code : null);

    [JsonIgnore]
    public bool IsWellFormed => !string.IsNullOrWhiteSpace(ClusterCode) && !string.IsNullOrWhiteSpace(Code)
        && new[] { ClusterCode, Code, CbuCode }.All(c => c is null || c.Length <= 50 && c == c.Trim())
        && (Level switch
        {
            OrgLevels.Cluster => Same(Code, ClusterCode) && CbuCode is null,
            OrgLevels.Cbu => !string.IsNullOrWhiteSpace(CbuCode) && Same(Code, CbuCode),
            OrgLevels.Branch or OrgLevels.OperationArea => !string.IsNullOrWhiteSpace(CbuCode),
            _ => false
        });

    public bool Contains(OrgLocation location) => IsWellFormed && Same(ClusterCode, location.ClusterCode)
        && (Level == OrgLevels.Cluster || Same(CbuCode, location.CbuCode))
        && (Level is OrgLevels.Cluster or OrgLevels.Cbu
            || Level == OrgLevels.Branch && Same(Code, location.BranchCode)
            || Level == OrgLevels.OperationArea && Same(Code, location.OperationAreaCode));

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
