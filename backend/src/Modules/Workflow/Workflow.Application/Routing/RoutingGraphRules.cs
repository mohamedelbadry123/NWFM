namespace Workflow.Application.Routing;

public static class RoutingGraphRules
{
    public static bool IsGate(string type) => type is "ExclusiveGateway" or "ParallelGateway" or "InclusiveGateway" or "JoinGateway";
    public static bool IsFork(string type) => type is "ParallelGateway" or "InclusiveGateway";

    public static HashSet<string> Region(RoutingGraph graph, string fork, string join)
    {
        var seen = new HashSet<string>(); var pending = new Stack<string>();
        var config = graph.Nodes.First(n => n.Key == fork).Routing;
        foreach (var edge in graph.Connections.Where(e => e.From == fork && e.Key != config?.FallbackTransitionKey)) pending.Push(edge.To);
        while (pending.TryPop(out var key))
        {
            if (key == join || key == fork || !seen.Add(key)) continue;
            foreach (var edge in graph.Connections.Where(e => e.From == key)) pending.Push(edge.To);
        }
        return seen;
    }

    public static IReadOnlyList<RoutingDiagnostic> Validate(RoutingGraph graph)
    {
        var errors = new List<RoutingDiagnostic>();
        void Error(string code, string message, string? node = null) => errors.Add(new(code, message, node, code == "ROUTING_JOIN_REQUIRED" ? "joinNodeKey" : null));
        if (graph.Nodes.Count > 500 || graph.Connections.Count > 1000) return [new("ROUTING_LIMIT", "The routing graph is too large.")];
        if (graph.Nodes.Any(n => !System.Text.RegularExpressions.Regex.IsMatch(n.Key ?? "", "^[A-Za-z0-9_-]{1,160}$"))
            || graph.Connections.Any(e => !System.Text.RegularExpressions.Regex.IsMatch(e.Key ?? "", "^[A-Za-z0-9_-]{1,160}$")))
            return [new("ROUTING_KEY", "Element and connection keys must use letters, numbers, underscores or hyphens (up to 160 characters).")];
        if (graph.Nodes.Select(n => n.Key).Distinct().Count() != graph.Nodes.Count)
            return [new("ROUTING_NODE_ID", "Each element needs a unique key.")];
        if (graph.Connections.Select(e => e.Key).Distinct().Count() != graph.Connections.Count)
            Error("ROUTING_EDGE_ID", "Each connection needs a unique key.");
        var nodes = graph.Nodes.ToDictionary(n => n.Key);
        if (graph.Nodes.Count(n => n.Type == "Start") != 1) Error("ROUTING_START", "Use exactly one Start.");
        if (!graph.Nodes.Any(n => n.Type == "End")) Error("ROUTING_END", "Add End.");
        foreach (var edge in graph.Connections)
            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To)) errors.Add(new("ROUTING_TARGET", "A connection references a missing element.", edge.From, ConnectionKey: edge.Key));
        foreach (var node in graph.Nodes)
        {
            var edges = graph.Connections.Where(e => e.From == node.Key).ToList();
            if (node.Type == "End" && edges.Count != 0) Error("ROUTING_END_EXIT", "End cannot have outgoing routes.", node.Key);
            if (IsGate(node.Type))
            {
                if (node.Routing is not { } config) { Error("ROUTING_CONFIG", "Configure this routing component.", node.Key); continue; }
                var issues = RoutingEvaluator.Validate(config);
                if (config.ContractVersion == 2 && node.Type != "ExclusiveGateway") Error("ROUTING_VERSION", "Ordered routing version 2 belongs to Conditions only.", node.Key);
                foreach (var message in issues) Error("ROUTING_RULE", message, node.Key);
                if (issues.Count > 0) continue;
                foreach (var key in config.Routes.Select(r => r.TransitionKey).Concat(new[] { config.FallbackTransitionKey, config.FailureTransitionKey }.OfType<string>()))
                    if (!edges.Any(e => e.Key == key)) errors.Add(new("ROUTING_DESTINATION", "Choose a destination for a route in " + node.Name + ".", node.Key, key));
                if (node.Type is "ExclusiveGateway" or "InclusiveGateway")
                {
                    if (config.Routes.Count == 0) Error("ROUTING_EMPTY", "Add a condition or explicitly choose Always.", node.Key);
                    if (edges.Any(e => !config.Routes.Any(r => r.TransitionKey == e.Key) && config.FallbackTransitionKey != e.Key && config.FailureTransitionKey != e.Key))
                        Error("ROUTING_UNCONFIGURED", "Every connection must be configured as a rule or fallback.", node.Key);
                    if (node.Type == "ExclusiveGateway" && config.MatchMode == "All") Error("ROUTING_FORK_REQUIRED", "Use a Fork to start multiple routes.", node.Key);
                    if (node.Type == "InclusiveGateway" && config.MatchMode != "All") Error("ROUTING_FORK_MODE", "A matching Fork must evaluate all matching branches.", node.Key);
                }
                if (node.Type == "JoinGateway" && edges.Count(e => e.Key != config.FailureTransitionKey) != 1)
                    Error("ROUTING_JOIN_EXIT", "A Join needs exactly one continuation, plus an optional failure route.", node.Key);
            }
            else if (node.Type != "End" && edges.Count != 1)
                Error("ROUTING_SINGLE_EXIT", "Choose one destination, or use a Condition or Fork for multiple routes.", node.Key);

        }
        var regions = new Dictionary<string, HashSet<string>>();
        foreach (var fork in graph.Nodes.Where(n => IsFork(n.Type)))
        {
            var join = fork.Routing?.JoinNodeKey;
            if (join is null || !nodes.TryGetValue(join, out var j) || j.Type != "JoinGateway")
            { Error("ROUTING_JOIN_REQUIRED", "Select this Fork's matching Join.", fork.Key); continue; }
            if (graph.Nodes.Count(n => IsFork(n.Type) && n.Routing?.JoinNodeKey == join) > 1)
                Error("ROUTING_JOIN_OWNER", "Each Join belongs to one Fork.", join);
            var branches = graph.Connections.Where(e => e.From == fork.Key && e.Key != fork.Routing?.FallbackTransitionKey).ToList();
            if (branches.Count < 2) Error("ROUTING_FORK_BRANCHES", "A Fork needs at least two branches.", fork.Key);
            foreach (var branch in branches)
            {
                var visited = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(branch.To);
                var reached = false;
                while (pending.TryPop(out var key))
                {
                    if (key == join) { reached = true; continue; }
                    if (!visited.Add(key) || !nodes.TryGetValue(key, out var current)) continue;
                    if (!regions.TryGetValue(key, out var membership)) regions[key] = membership = [];
                    if (membership.Any(m => m.StartsWith(fork.Key + ":", StringComparison.Ordinal) && m != fork.Key + ":" + branch.Key))
                        Error("ROUTING_BRANCH_CROSSING", "Branches must meet at their Join, not at a shared Step or Event.", key);
                    membership.Add(fork.Key + ":" + branch.Key);
                    if (key == fork.Key || current.Type == "End") Error("ROUTING_JOIN_BYPASS", "Finish this fork cycle at its Join before leaving or restarting it.", key);
                    foreach (var e in graph.Connections.Where(e => e.From == key)) pending.Push(e.To);
                }
                if (!reached) Error("ROUTING_JOIN_UNREACHABLE", "Connect the branch beginning at " + nodes[branch.To].Name + " to " + nodes[join].Name + " before continuing.", branch.To);
                foreach (var incoming in graph.Connections.Where(e => visited.Contains(e.To) && !visited.Contains(e.From)))
                    if (incoming.Key != branch.Key) errors.Add(new("ROUTING_BRANCH_ENTRY", "Enter a branch through its Fork; routes cannot cross into another group.", incoming.From, ConnectionKey: incoming.Key));
            }
            var group = regions.Where(p => p.Value.Any(m => m.StartsWith(fork.Key + ":", StringComparison.Ordinal))).Select(p => p.Key).ToHashSet();
            foreach (var incoming in graph.Connections.Where(e => e.To == join && !group.Contains(e.From)))
                if (incoming.From != fork.Key || incoming.Key == fork.Routing?.FallbackTransitionKey)
                    errors.Add(new("ROUTING_JOIN_ENTRY", "Only this Fork's activated branches may enter its Join.", incoming.From, ConnectionKey: incoming.Key));
        }
        foreach (var join in graph.Nodes.Where(n => n.Type == "JoinGateway"))
            if (!graph.Nodes.Any(n => IsFork(n.Type) && n.Routing?.JoinNodeKey == join.Key))
                Error("ROUTING_ORPHAN_JOIN", "Select this Join from its Fork.", join.Key);
        return errors.Distinct().ToList();
    }
}
