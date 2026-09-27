namespace NWFM.Shared.Organization;

/// <summary>
/// One row of an owner's coverage — a place, a kind of work, or both. A null <see cref="Level"/> /
/// <see cref="Code"/> means every territory; a null <see cref="DepartmentCode"/> means every
/// department.
/// </summary>
public sealed record OrgScopeRow(string? Level, string? Code, string? DepartmentCode);

/// <summary>
/// One department's coverage flattened into plain code lists, for a caller that must filter in the
/// database. <see cref="OrgScopeSet.Covers"/> answers the same question for a row already loaded;
/// this hands the codes to a query so the rows never have to be.
/// </summary>
public sealed record OrgScopeTerritory(
    string? DepartmentCode,
    bool CoversAllTerritory,
    IReadOnlyList<string> CbuCodes,
    IReadOnlyList<string> BranchCodes,
    IReadOnlyList<string> OperationAreaCodes);

/// <summary>
/// The expanded coverage an owner — a user or a team — holds: its scope rows grouped by department,
/// each group carrying every CBU, branch and operation area code its rows reach.
///
/// Grouped by department rather than flattened because the two axes are not independent: an owner
/// working water in Riyadh and waste-water in Jeddah must not thereby be given water in Jeddah.
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

        // "" stands in for "no department" so one dictionary keys both cases.
        foreach (var row in list)
        {
            var key = row.DepartmentCode?.Trim() ?? string.Empty;

            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group(string.IsNullOrEmpty(key) ? null : key);
                groups[key] = group;
            }

            if (string.IsNullOrWhiteSpace(row.Level) || string.IsNullOrWhiteSpace(row.Code))
            {
                // A department-only row: that kind of work, wherever it is.
                group.CoversAllTerritory = true;
                continue;
            }

            Expand(group, row.Level.Trim(), row.Code.Trim(), hierarchy);
        }

        return new OrgScopeSet(false, [.. groups.Values]);
    }

    /// <summary>
    /// True when this coverage reaches the given work. Both axes must be satisfied by the <b>same</b>
    /// group — the reason for grouping by department. On the location axis, reaching the work through
    /// any one of CBU, branch or operation area is enough, since work need not carry every level.
    /// </summary>
    public bool Covers(string? cbuCode, string? branchCode, string? operationAreaCode, string? departmentCode) =>
        IsUnrestricted
        || _groups.Any(group =>
            group.AcceptsDepartment(departmentCode)
            && group.AcceptsLocation(cbuCode, branchCode, operationAreaCode));

    /// <summary>True when this coverage and <paramref name="other"/> share any work at all.</summary>
    public bool Overlaps(OrgScopeSet other) =>
        IsUnrestricted
        || other.IsUnrestricted
        || _groups.Any(group => other._groups.Any(otherGroup =>
            group.AcceptsDepartment(otherGroup.DepartmentCode) && group.OverlapsLocation(otherGroup)));

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
            [.. group.OperationAreaCodes]))];

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

    private sealed class Group(string? departmentCode)
    {
        public string? DepartmentCode { get; } = departmentCode;

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
