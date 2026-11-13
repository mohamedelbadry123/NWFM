namespace Workflow.Application.Routing;

/// <summary>Only nodes completed in this branch can replace values at its Join.</summary>
public static class RoutingData
{
    public static void ClearNodes(IDictionary<string, object?> values, IEnumerable<string> nodes)
    {
        foreach (var node in nodes)
            foreach (var key in values.Keys.Where(k => k.StartsWith(node + ".", StringComparison.Ordinal)).ToList()) values.Remove(key);
    }
    public static void ReplaceNode(IDictionary<string, object?> target, string node, IReadOnlyDictionary<string, object?> fields, string outcome)
    {
        foreach (var key in target.Keys.Where(k => k.StartsWith(node + ".", StringComparison.Ordinal)).ToList()) target.Remove(key);
        foreach (var pair in fields) target[node + "." + pair.Key] = pair.Value;
        target[node + ".$outcome"] = outcome;
        target["$lastResultNode"] = node;
    }

    public static void Merge(IDictionary<string, object?> target, IReadOnlyDictionary<string, object?> branch, IEnumerable<string> changedNodes)
    {
        foreach (var node in changedNodes)
        {
            var prefix = node + ".";
            foreach (var key in target.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) target.Remove(key);
            foreach (var pair in branch.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))) target[pair.Key] = pair.Value;
        }
    }

    public static void JoinOutput(IDictionary<string, object?> target, string join, IReadOnlyDictionary<string, object?>? winner, IReadOnlyCollection<string>? changed)
    {
        var fields = new Dictionary<string, object?>();
        if (winner is not null && winner.TryGetValue("$lastResultNode", out var raw) && raw?.ToString() is string node && changed?.Contains(node) == true)
        {
            fields["$winnerNode"] = node;
            foreach (var pair in winner.Where(p => p.Key.StartsWith(node + ".", StringComparison.Ordinal) && !p.Key.EndsWith(".$outcome", StringComparison.Ordinal))) fields[pair.Key[(node.Length + 1)..]] = pair.Value;
        }
        ReplaceNode(target, join, fields, "success");
    }
}
