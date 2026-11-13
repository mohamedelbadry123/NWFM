using NWFM.Shared.Organization;

namespace Workflow.Domain.Entities;

/// <summary>
/// One entry as its author built it: a Cluster, optionally a CBU under it, and optionally a Branch and an
/// Operation Area under that CBU. Rows only remember how the canonical <see cref="WorkflowOrganizationScope"/>
/// list was assembled so an editor can show the same entries again; they never widen or narrow what the
/// workflow covers — the canonical list stays authoritative.
/// </summary>
public sealed record WorkflowScopeRow(string Id, string ClusterCode, string? CbuCode = null, string? BranchCode = null, string? OperationAreaCode = null)
{
    /// <summary>Scopes a row expands to: the deepest level chosen, with a Branch and an Operation Area each standing as their own scope (a union, not an intersection).</summary>
    public IReadOnlyList<WorkflowOrganizationScope> Expand()
    {
        if (string.IsNullOrWhiteSpace(CbuCode)) return [new(OrgLevels.Cluster, ClusterCode, ClusterCode)];
        var scopes = new List<WorkflowOrganizationScope>();
        if (!string.IsNullOrWhiteSpace(BranchCode)) scopes.Add(new(OrgLevels.Branch, BranchCode, ClusterCode, CbuCode));
        if (!string.IsNullOrWhiteSpace(OperationAreaCode)) scopes.Add(new(OrgLevels.OperationArea, OperationAreaCode, ClusterCode, CbuCode));
        if (scopes.Count == 0) scopes.Add(new(OrgLevels.Cbu, CbuCode, ClusterCode, CbuCode));
        return scopes;
    }

    /// <summary>The entry's identity for duplicate detection; codes compare without regard to case.</summary>
    public string Key => string.Join('|', new[] { ClusterCode, CbuCode, BranchCode, OperationAreaCode }.Select(c => (c ?? "").Trim().ToUpperInvariant()));
}

/// <summary>Checks that persisted scope rows are well formed and expand to exactly the canonical scope list.</summary>
public static class WorkflowScopeRows
{
    public const int MaxRows = 500;
    public const int MaxIdLength = 64;
    public const string InvalidCode = "WORKSPACE_SCOPE_ROWS";

    /// <summary>
    /// Null when there are no rows (older saves) or they agree with the canonical scopes; otherwise what is wrong.
    /// Rows need not list scopes in the same order, and an entry that overlaps another (a CBU inside a whole
    /// Cluster, say) still has to appear in the canonical list — nothing authored may be dropped.
    /// </summary>
    public static string? Validate(WorkflowWorkspaceDefinition? workspace)
    {
        if (workspace?.ScopeRows is not { } rows) return null;
        if (workspace.OrganizationScopes is null) return "Scope entries need the organization scopes they describe.";
        if (rows.Count > MaxRows) return $"Use at most {MaxRows} scope entries.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is null) return "Each scope entry must be complete.";
            if (string.IsNullOrWhiteSpace(row.Id) || row.Id.Length > MaxIdLength || !ids.Add(row.Id)) return "Every scope entry needs its own identifier.";
            if (string.IsNullOrWhiteSpace(row.ClusterCode)) return "Each scope entry needs a Cluster.";
            if ((string.IsNullOrWhiteSpace(row.CbuCode) && (!string.IsNullOrWhiteSpace(row.BranchCode) || !string.IsNullOrWhiteSpace(row.OperationAreaCode)))
                || new[] { row.ClusterCode, row.CbuCode, row.BranchCode, row.OperationAreaCode }.Any(c => c is not null && (c.Length > 50 || c != c.Trim())))
                return "A Branch or Operation Area needs a CBU, and every code must be trimmed.";
            if (!keys.Add(row.Key)) return "The same scope entry is listed twice.";
        }
        var authored = rows.SelectMany(r => r.Expand()).Select(Identity).ToHashSet(StringComparer.Ordinal);
        var canonical = workspace.OrganizationScopes.Where(s => s is not null).Select(Identity).ToHashSet(StringComparer.Ordinal);
        return authored.SetEquals(canonical) ? null : "The scope entries do not match the organization scope they describe.";
    }

    private static string Identity(WorkflowOrganizationScope s) =>
        string.Join('|', s.Level, (s.Code ?? "").ToUpperInvariant(), (s.ClusterCode ?? "").ToUpperInvariant(), (s.CbuCode ?? "").ToUpperInvariant());
}
