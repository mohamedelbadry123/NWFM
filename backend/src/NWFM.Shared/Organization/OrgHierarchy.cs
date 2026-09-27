namespace NWFM.Shared.Organization;

/// <summary>
/// The org levels a scope can name. Branch and operation area are both direct children of a CBU —
/// siblings, not a chain — so a branch scope reaches branch-stamped work and nothing beneath it.
/// Kept in step with Auth's own <c>OrgScopeLevels</c>, whose values are what the scope rows store.
/// </summary>
public static class OrgLevels
{
    public const string Cluster = nameof(Cluster);
    public const string Cbu = nameof(Cbu);
    public const string Branch = nameof(Branch);
    public const string OperationArea = nameof(OperationArea);
}

/// <summary>
/// The parent chain of every org unit, flattened once so a scope assigned at one level can be
/// expanded to the codes beneath it without walking four tables per request.
///
/// Plain dictionaries with normalised keys, not case-insensitive comparers: the hierarchy is cached,
/// and a serialised dictionary does not keep its comparer across the round trip.
/// </summary>
public sealed class OrgHierarchy
{
    public Dictionary<string, List<string>> CbusByCluster { get; init; } = [];
    public Dictionary<string, List<string>> BranchesByCbu { get; init; } = [];
    public Dictionary<string, List<string>> AreasByCbu { get; init; } = [];
    public Dictionary<string, string> ClusterByCbu { get; init; } = [];

    public static OrgHierarchy Empty { get; } = new();

    public IReadOnlyList<string> CbusUnderCluster(string clusterCode) => Lookup(CbusByCluster, clusterCode);

    public IReadOnlyList<string> BranchesUnderCbu(string cbuCode) => Lookup(BranchesByCbu, cbuCode);

    public IReadOnlyList<string> AreasUnderCbu(string cbuCode) => Lookup(AreasByCbu, cbuCode);

    public string? ClusterOfCbu(string? cbuCode) =>
        cbuCode is not null && ClusterByCbu.TryGetValue(Normalize(cbuCode), out var cluster) ? cluster : null;

    /// <summary>Builds the maps from the four lookup tables' rows.</summary>
    public static OrgHierarchy Build(
        IEnumerable<(string Code, string? ClusterCode)> cbus,
        IEnumerable<(string Code, string? CbuCode)> branches,
        IEnumerable<(string Code, string? CbuCode)> operationAreas)
    {
        var hierarchy = new OrgHierarchy();

        foreach (var (code, clusterCode) in cbus)
        {
            if (string.IsNullOrWhiteSpace(clusterCode))
            {
                continue;
            }

            Add(hierarchy.CbusByCluster, clusterCode, code);
            hierarchy.ClusterByCbu[Normalize(code)] = clusterCode;
        }

        foreach (var (code, cbuCode) in branches)
        {
            if (!string.IsNullOrWhiteSpace(cbuCode))
            {
                Add(hierarchy.BranchesByCbu, cbuCode, code);
            }
        }

        foreach (var (code, cbuCode) in operationAreas)
        {
            if (!string.IsNullOrWhiteSpace(cbuCode))
            {
                Add(hierarchy.AreasByCbu, cbuCode, code);
            }
        }

        return hierarchy;
    }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    private static void Add(Dictionary<string, List<string>> map, string parent, string child)
    {
        var key = Normalize(parent);

        if (!map.TryGetValue(key, out var children))
        {
            children = [];
            map[key] = children;
        }

        children.Add(child);
    }

    private static IReadOnlyList<string> Lookup(Dictionary<string, List<string>> map, string code) =>
        map.TryGetValue(Normalize(code), out var children) ? children : [];
}
