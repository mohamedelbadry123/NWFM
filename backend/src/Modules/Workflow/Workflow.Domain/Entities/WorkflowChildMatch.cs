namespace Workflow.Domain.Entities;

/// <summary>Definition identity matching, deliberately stricter than execution-location coverage.</summary>
public static class WorkflowChildMatch
{
    public static WorkflowWorkspaceDefinition? ReadChild(string? json)
    {
        try { return WorkflowWorkspaceDefinition.Read(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }
    public static bool SameScope(WorkflowWorkspaceDefinition? parent, WorkflowWorkspaceDefinition? child)
    {
        if (parent is not { HasScopeSettings: true, HasRequiredLocation: true, HasLegacyConflict: false }
            || child is not { HasScopeSettings: true, HasRequiredLocation: true, HasLegacyConflict: false }
            || !parent.Location.IsEmpty || !child.Location.IsEmpty) return false;
        static string Key(WorkflowOrganizationScope s) => System.Text.Json.JsonSerializer.Serialize(
            new[] { s.Level, s.Code.ToUpperInvariant(), s.ClusterCode.ToUpperInvariant(), s.CbuCode?.ToUpperInvariant() });
        return parent.OrganizationScopes!.Select(Key).ToHashSet(StringComparer.Ordinal)
            .SetEquals(child.OrganizationScopes!.Select(Key));
    }

    public static bool Matches(WorkflowWorkspaceDefinition? parent, WorkflowWorkspaceDefinition? child, Guid? taskTypeId) =>
        taskTypeId is not null && child?.Kind == WorkflowWorkspaceDefinition.ChildKind
        && child.TaskTypeId == taskTypeId && SameScope(parent, child);
}
