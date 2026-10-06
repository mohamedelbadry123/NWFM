using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NWFM.Shared.Organization;

namespace Workflow.Domain.Entities;

/// <summary>
/// Versioned business context. Null on legacy definitions for backwards compatibility.
/// New definitions hold eligible organization scopes; each execution resolves one location.
/// Legacy definitions retain their single location and child inheritance behavior.
/// </summary>
public sealed record WorkflowWorkspaceDefinition(
    string Kind,
    string? ClusterCode = null,
    string? CbuCode = null,
    string? BranchCode = null,
    string? OperationAreaCode = null,
    int DesignerVersion = 1)
{
    public const string MainKind = "Main";
    public const string ChildKind = "Child";

    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<WorkflowOrganizationScope>? OrganizationScopes { get; init; }
    public Guid? FieldActivityTypeId { get; init; }
    public string? DepartmentCode { get; init; }
    public string? FieldActivityCode { get; init; }
    public Guid? TaskTypeId { get; init; }

    [JsonIgnore]
    public bool HasScopeSettings => SchemaVersion >= 2 || OrganizationScopes is not null;

    [JsonIgnore]
    public OrgLocation? DefaultLocation => HasScopeSettings
        ? OrganizationScopes is { Count: 1 } scopes && scopes[0] is { IsWellFormed: true } ? scopes[0].Location : null
        : Kind == MainKind ? Location : null;

    public bool AllowsLocation(OrgLocation location) => !HasLegacyConflict && (HasScopeSettings
        ? OrganizationScopes?.Any(s => s is not null && s.Contains(location)) == true
        : Kind == ChildKind || Location == location.Normalized());

    [JsonIgnore]
    public OrgLocation Location => new OrgLocation(ClusterCode, CbuCode, BranchCode, OperationAreaCode).Normalized();

    /// <summary>
    /// True when the stored settings carry both a pre-hierarchy key and its replacement with different
    /// codes. Neither is chosen: the workflow cannot be published or started until someone re-selects
    /// its location.
    /// </summary>
    [JsonIgnore]
    public bool HasLegacyConflict { get; init; }

    /// <summary>New scopes may stop at Cluster; legacy main locations still require Cluster and CBU.</summary>
    [JsonIgnore]
    public bool HasRequiredLocation => HasScopeSettings
        ? OrganizationScopes is { Count: > 0 and <= 500 } && OrganizationScopes.All(s => s is { IsWellFormed: true })
        : Location is { ClusterCode: not null, CbuCode: not null };

    /// <summary>
    /// Reads stored settings, whichever shape they were written in.
    ///
    /// Compatibility adapter: before the shared org hierarchy, the workspace stored
    /// <c>regionCode</c> and <c>cityCode</c>. Those were only ever chosen from, and validated against,
    /// Auth's CBU and Branch lookups (a region was a CBU under the cluster, a city a branch under the
    /// CBU), so they are read here as <c>cbuCode</c> and <c>branchCode</c>. Published versions keep
    /// their original JSON — it is part of their content hash — so this stays until no published or
    /// draft version still carries the old keys; then it can go, leaving plain deserialisation.
    /// </summary>
    /// <exception cref="JsonException">The settings are not a JSON object.</exception>
    public static WorkflowWorkspaceDefinition? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) is not JsonObject settings)
        {
            return json.Trim() == "null" ? null : throw new JsonException("Workflow settings must be a JSON object.");
        }

        string? Text(string name) => settings[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

        var conflict = false;
        string? Merge(string current, string legacy)
        {
            var (now, old) = (Text(current), Text(legacy));
            if (now is not null && old is not null && !string.Equals(now, old, StringComparison.OrdinalIgnoreCase))
            {
                conflict = true;
            }

            return now ?? old;
        }

        var designerVersion = settings["designerVersion"] is JsonValue version && version.TryGetValue<int>(out var number) ? number : 1;

        return new WorkflowWorkspaceDefinition(
            Text("kind") ?? string.Empty,
            Text("clusterCode"),
            Merge("cbuCode", "regionCode"),
            Merge("branchCode", "cityCode"),
            Text("operationAreaCode"),
            designerVersion)
        {
            HasLegacyConflict = conflict,
            SchemaVersion = settings["schemaVersion"]?.Deserialize<int?>() ?? 1,
            OrganizationScopes = settings["organizationScopes"]?.Deserialize<List<WorkflowOrganizationScope>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            FieldActivityTypeId = settings["fieldActivityTypeId"]?.Deserialize<Guid?>(),
            DepartmentCode = Text("departmentCode"),
            FieldActivityCode = Text("fieldActivityCode"),
            TaskTypeId = settings["taskTypeId"]?.Deserialize<Guid?>(),
        };
    }

    /// <summary>Reads just the kind, treating unreadable settings as having none.</summary>
    public static string? KindOf(string? json)
    {
        try { return Read(json)?.Kind; }
        catch (JsonException) { return null; }
    }
}
