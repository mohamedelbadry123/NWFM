namespace NWFM.Shared.Organization;

/// <summary>
/// One row of an owner's coverage — a place, kinds of work, or both. A null <see cref="Level"/> /
/// <see cref="Code"/> means every territory; no <see cref="DepartmentCodes"/> means every department;
/// no <see cref="ActivityTypeCodes"/> means every activity type.
/// </summary>
public sealed record OrgScopeRow(
    string? Level,
    string? Code,
    IReadOnlyList<string> DepartmentCodes,
    IReadOnlyList<string> ActivityTypeCodes)
{
    /// <summary>A row for one department, or for all of them when null, and every activity type.</summary>
    public OrgScopeRow(string? level, string? code, string? departmentCode)
        : this(level, code, string.IsNullOrWhiteSpace(departmentCode) ? [] : [departmentCode], [])
    {
    }
}

/// <summary>
/// One department's coverage flattened into plain code lists, for a caller that must filter in the
/// database. <see cref="OrgScopeSet.Covers"/> answers the same question for a row already loaded;
/// this hands the codes to a query so the rows never have to be. <see cref="ActivityTypeCodes"/> is
/// empty when the group takes every activity type.
/// </summary>
public sealed record OrgScopeTerritory(
    string? DepartmentCode,
    bool CoversAllTerritory,
    IReadOnlyList<string> CbuCodes,
    IReadOnlyList<string> BranchCodes,
    IReadOnlyList<string> OperationAreaCodes,
    IReadOnlyList<string> ActivityTypeCodes);

/// <summary>
/// The expanded coverage an owner — a user or a team — holds: its scope rows grouped by department
/// (and the activity types the row names), each group carrying every CBU, branch and operation area
/// code its rows reach. A row naming several departments counts as one row per department.
///
/// Grouped rather than flattened because the axes are not independent: an owner working water in
/// Riyadh and waste-water in Jeddah must not thereby be given water in Jeddah.
///
/// An owner with no rows at all is <see cref="IsUnrestricted"/>, not "covers nothing" — coverage can
/// be rolled out gradually without anyone's worklist going blank the moment it is enforced.
/// </summary>
public sealed class OrgScopeSet
{
    private readonly List<Group> _groups;

    private OrgScopeSet(bool isUnrestricted, List<Group> groups)
    {
        IsUnrestricted = isUnrestricted;
        _groups = groups;
    }

    public bool IsUnrestricted { get; }

    public static OrgScopeSet Unrestricted() => new(true, []);

    public static OrgScopeSet FromRows(IEnumerable<OrgScopeRow> rows, OrgHierarchy hierarchy)
    {
        var list = rows.ToList();

        if (list.Count == 0)
        {
            return Unrestricted();
        }

        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in list)
        {
            var activityTypes = Clean(row.ActivityTypeCodes);
            var departments = Clean(row.DepartmentCodes);

            // A row with no department is one group taking every department.
            IEnumerable<string?> targets = departments.Count == 0 ? [null] : departments;

            foreach (var department in targets)
            {
                // "" stands in for "no department", and the activity types join the key, so rows that
                // cover the same kinds of work share one group and their territories union.
                var key = (department ?? string.Empty) + "|" + string.Join(",", activityTypes.Order(StringComparer.OrdinalIgnoreCase));

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Group(department, activityTypes);
                    groups[key] = group;
                }

                if (string.IsNullOrWhiteSpace(row.Level) || string.IsNullOrWhiteSpace(row.Code))
                {
                    // A row with no territory: those kinds of work, wherever they are.
                    group.CoversAllTerritory = true;
                    continue;
                }

                Expand(group, row.Level.Trim(), row.Code.Trim(), hierarchy);
            }
        }

        return new OrgScopeSet(false, [.. groups.Values]);
    }

    /// <summary>
    /// True when this coverage reaches the given work. Every axis must be satisfied by the <b>same</b>
    /// group — the reason for grouping. On the location axis, reaching the work through any one of
    /// CBU, branch or operation area is enough, since work need not carry every level. Work with no
    /// activity type (a task, say) is taken by any group, as work with no department is.
    /// </summary>
    public bool Covers(
        string? cbuCode,
        string? branchCode,
        string? operationAreaCode,
        string? departmentCode,
        string? activityTypeCode = null) =>
        IsUnrestricted
        || _groups.Any(group =>
            group.AcceptsDepartment(departmentCode)
            && group.AcceptsActivityType(activityTypeCode)
            && group.AcceptsLocation(cbuCode, branchCode, operationAreaCode));

    /// <summary>True when this coverage and <paramref name="other"/> share any work at all.</summary>
    public bool Overlaps(OrgScopeSet other) =>
        IsUnrestricted
        || other.IsUnrestricted
        || _groups.Any(group => other._groups.Any(otherGroup =>
            group.AcceptsDepartment(otherGroup.DepartmentCode)
            && group.OverlapsActivityTypes(otherGroup)
            && group.OverlapsLocation(otherGroup)));

    /// <summary>
    /// The coverage as one set of code lists per department group — what a query needs to narrow a
    /// worklist without materialising every candidate row. Clusters are left out: work is stamped
    /// with CBU, branch and operation area, and a cluster scope is already expanded into its CBUs.
    /// </summary>
    public IReadOnlyList<OrgScopeTerritory> ToTerritories() =>
        [.. _groups.Select(group => new OrgScopeTerritory(
            group.DepartmentCode,
            group.CoversAllTerritory,
            [.. group.CbuCodes],
            [.. group.BranchCodes],
            [.. group.OperationAreaCodes],
            [.. group.ActivityTypeCodes]))];

    private static List<string> Clean(IReadOnlyList<string>? codes) =>
        (codes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void Expand(Group group, string level, string code, OrgHierarchy hierarchy)
    {
        switch (level)
        {
            case OrgLevels.Cluster:
                group.ClusterCodes.Add(code);
                foreach (var cbu in hierarchy.CbusUnderCluster(code))
                {
                    group.CbuCodes.Add(cbu);
                    ExpandCbu(group, cbu, hierarchy);
                }

                break;

            case OrgLevels.Cbu:
                group.CbuCodes.Add(code);
                ExpandCbu(group, code, hierarchy);
                break;

            // Nothing hangs below a branch: operation areas are its siblings under the CBU.
            case OrgLevels.Branch:
                group.BranchCodes.Add(code);
                break;

            case OrgLevels.OperationArea:
                group.OperationAreaCodes.Add(code);
                break;
        }
    }

    /// <summary>Adds both leaf levels beneath a CBU — they are parallel, not nested.</summary>
    private static void ExpandCbu(Group group, string cbuCode, OrgHierarchy hierarchy)
    {
        foreach (var branch in hierarchy.BranchesUnderCbu(cbuCode))
        {
            group.BranchCodes.Add(branch);
        }

        foreach (var area in hierarchy.AreasUnderCbu(cbuCode))
        {
            group.OperationAreaCodes.Add(area);
        }
    }

    private sealed class Group(string? departmentCode, IEnumerable<string> activityTypeCodes)
    {
        public string? DepartmentCode { get; } = departmentCode;

        /// <summary>The activity types the group takes; empty for every one.</summary>
        public HashSet<string> ActivityTypeCodes { get; } = new(activityTypeCodes, StringComparer.OrdinalIgnoreCase);

        /// <summary>Set by a department-only row: that department, in every territory.</summary>
        public bool CoversAllTerritory { get; set; }

        public HashSet<string> ClusterCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CbuCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BranchCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OperationAreaCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A group with no department takes any work; work with no department is taken by any group,
        /// because the department is optional on work and hiding unclassified work would lose it.
        /// </summary>
        public bool AcceptsDepartment(string? departmentCode) =>
            DepartmentCode is null
            || string.IsNullOrWhiteSpace(departmentCode)
            || string.Equals(DepartmentCode, departmentCode.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The same rule as the department: no list takes any type, work with no type is taken.</summary>
        public bool AcceptsActivityType(string? activityTypeCode) =>
            ActivityTypeCodes.Count == 0
            || string.IsNullOrWhiteSpace(activityTypeCode)
            || ActivityTypeCodes.Contains(activityTypeCode.Trim());

        public bool OverlapsActivityTypes(Group other) =>
            ActivityTypeCodes.Count == 0
            || other.ActivityTypeCodes.Count == 0
            || ActivityTypeCodes.Overlaps(other.ActivityTypeCodes);

        public bool AcceptsLocation(string? cbuCode, string? branchCode, string? operationAreaCode) =>
            CoversAllTerritory
            || (cbuCode is not null && CbuCodes.Contains(cbuCode))
            || (branchCode is not null && BranchCodes.Contains(branchCode))
            || (operationAreaCode is not null && OperationAreaCodes.Contains(operationAreaCode));

        public bool OverlapsLocation(Group other) =>
            CoversAllTerritory
            || other.CoversAllTerritory
            || CbuCodes.Overlaps(other.CbuCodes)
            || BranchCodes.Overlaps(other.BranchCodes)
            || OperationAreaCodes.Overlaps(other.OperationAreaCodes)
            || ClusterCodes.Overlaps(other.ClusterCodes);
    }
}
