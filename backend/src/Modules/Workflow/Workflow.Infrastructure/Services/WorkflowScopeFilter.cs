using System.Linq.Expressions;
using NWFM.Shared.Organization;
using Workflow.Domain.Entities;

namespace Workflow.Infrastructure.Services;

/// <summary>
/// Which workflow instances a caller's coverage reaches — the shared <see cref="OrgScopeSet"/> rule,
/// applied to the location every instance carries. Coverage is who may see and act; the location is
/// only where the work is, so the two never stand in for each other.
/// </summary>
internal static class WorkflowScopeFilter
{
    /// <summary>
    /// The coverage as a <c>WHERE</c> clause. An instance has no department of its own, and work with
    /// no department is taken by every department group, so the groups' territories simply union; a
    /// group covering all territory reaches every instance. An instance with no location is reached
    /// only by unrestricted coverage, never assumed to be everyone's.
    /// </summary>
    public static Expression<Func<WorkflowInstance, bool>> ForScope(OrgScopeSet scope)
    {
        if (scope.IsUnrestricted)
        {
            return _ => true;
        }

        var territories = scope.ToTerritories();
        if (territories.Any(t => t.CoversAllTerritory))
        {
            return _ => true;
        }

        var cbuCodes = territories.SelectMany(t => t.CbuCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var branchCodes = territories.SelectMany(t => t.BranchCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var areaCodes = territories.SelectMany(t => t.OperationAreaCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var clusterCodes = territories.SelectMany(t => t.ClusterCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return i => (i.CbuCode != null && cbuCodes.Contains(i.CbuCode))
            || (i.BranchCode != null && branchCodes.Contains(i.BranchCode))
            || (i.OperationAreaCode != null && areaCodes.Contains(i.OperationAreaCode))
            || (i.CbuCode == null && i.BranchCode == null && i.OperationAreaCode == null
                && i.ClusterCode != null && clusterCodes.Contains(i.ClusterCode));
    }

    /// <summary>
    /// The same rule for an instance already loaded. With <paramref name="departmentCode"/> and
    /// <paramref name="activityTypeCode"/> — those of the activity being worked — the coverage must reach
    /// the place and both kinds of work in one group.
    /// </summary>
    public static bool Allows(WorkflowInstance instance, OrgScopeSet scope, string? departmentCode = null, string? activityTypeCode = null) =>
        instance.Location.IsCoveredBy(scope, departmentCode, activityTypeCode);
}
