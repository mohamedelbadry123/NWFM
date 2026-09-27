using System.Linq.Expressions;
using NWFM.Shared.Organization;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Common;

/// <summary>
/// Who may see which task. One place, so the list, the map, the detail and every command answer
/// "may this caller touch this task" the same way — two copies of the rule are two chances for one
/// of them to be the loose one. The reference app checked scope on its lists but not on opening a
/// survey by id; every entry point here goes through this.
/// </summary>
internal static class TaskScopeFilter
{
    /// <summary>
    /// The caller's coverage as one <c>WHERE</c> clause: an <c>OR</c> across their department groups,
    /// each pairing a department with the territory it reaches. ANDing "my departments" against "my
    /// territories" instead would grant the cross product — water in one place and waste-water in
    /// another would become both in both.
    /// </summary>
    public static Expression<Func<FieldTask, bool>> ForScope(OrgScopeSet scope)
    {
        if (scope.IsUnrestricted)
        {
            return _ => true;
        }

        var predicate = PredicateBuilder.False<FieldTask>();

        foreach (var territory in scope.ToTerritories())
        {
            var cbuCodes = territory.CbuCodes.ToList();
            var branchCodes = territory.BranchCodes.ToList();
            var areaCodes = territory.OperationAreaCodes.ToList();
            var departmentCode = territory.DepartmentCode;

            // Work with no department is admitted by every group: the field is optional, and hiding
            // unclassified work would lose it rather than protect it.
            Expression<Func<FieldTask, bool>> matchesDepartment = departmentCode is null
                ? _ => true
                : t => t.DepartmentCode == null || t.DepartmentCode == departmentCode;

            Expression<Func<FieldTask, bool>> matchesLocation = territory.CoversAllTerritory
                ? _ => true
                : t => (t.CbuCode != null && cbuCodes.Contains(t.CbuCode))
                    || (t.BranchCode != null && branchCodes.Contains(t.BranchCode))
                    || (t.OperationAreaCode != null && areaCodes.Contains(t.OperationAreaCode));

            predicate = predicate.Or(matchesDepartment.And(matchesLocation));
        }

        return predicate;
    }

    /// <summary>
    /// A crew sees the tasks its team has been handed — current or finished, but not ones since moved
    /// to another team. Territory alone would show it every task in its area, including other crews'.
    /// </summary>
    public static Expression<Func<FieldTask, bool>> ForTeam(Guid teamId) =>
        t => t.Assignments.Any(a => a.TeamId == teamId && a.Status != TaskAssignmentStatuses.Reassigned);

    /// <summary>The same rule for a task already loaded.</summary>
    public static bool Allows(FieldTask task, OrgScopeSet scope, Guid? callerTeamId)
    {
        if (!scope.Covers(task.CbuCode, task.BranchCode, task.OperationAreaCode, task.DepartmentCode))
        {
            return false;
        }

        return callerTeamId is not Guid teamId
            || task.Assignments.Any(a => a.TeamId == teamId && a.Status != TaskAssignmentStatuses.Reassigned);
    }
}
