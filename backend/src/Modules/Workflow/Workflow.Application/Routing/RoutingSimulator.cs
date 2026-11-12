namespace Workflow.Application.Routing;

/// <summary>Deterministic, side-effect-free orchestration over mocked external/human completions.</summary>
public static class RoutingSimulator
{
    private sealed class Flow(string id, Dictionary<string, object?> values, Gate? gate = null)
    {
        public string Id = id;
        public Dictionary<string, object?> Values = values;
        public Gate? Gate = gate;
        public HashSet<string> Changed = [];
    }
    private sealed class Gate(RoutingNode fork, RoutingNode join, Flow parent, int at)
    {
        public RoutingNode Fork = fork;
        public RoutingNode Join = join;
        public Flow Parent = parent;
        public int At = at;
        public bool Released;
        public bool Stopped;
        public Dictionary<string, string> States = [];
        public Dictionary<string, Flow> Branches = [];
    }

    public static RoutingSimulationResult Run(RoutingGraph graph, RoutingSimulationInput input)
    {
        var errors = RoutingGraphRules.Validate(graph);
        if (errors.Count > 0) return new([], [], errors);
        if (input.Signals.Count > 500) return new([], [], [new("SIMULATION_LIMIT", "Use at most 500 simulation signals.")]);
        var nodes = graph.Nodes.ToDictionary(n => n.Key);
        var trace = new List<RoutingTrace>();
        var pending = new List<(RoutingNode Node, Flow Flow)>();
        var gates = new List<Gate>();
        var queue = new Queue<(string Node, Flow Flow)>();
        long clock = 0; var count = 0; var cycle = 0;
        var values = (input.Inputs ?? []).ToDictionary(p => "$input." + p.Key, p => (object?)p.Value);
        var start = graph.Nodes.FirstOrDefault(n => n.Type == "Start");
        if (start is null) return new([], [], [new("NO_START_NODE", "Add Start before simulating.")]);
        queue.Enqueue((start.Key, new("root", values)));
        void Follow(RoutingNode node, Flow flow, string? key = null)
        {
            var next = graph.Connections.FirstOrDefault(e => e.From == node.Key && (key is null || e.Key == key));
            if (next is null) { trace.Add(new(node.Key, "Attention", "No configured continuation.", flow.Id)); return; }
            queue.Enqueue((next.To, flow));
        }
        bool Cancelled(Flow flow) => flow.Gate is { } gate && (gate.States.GetValueOrDefault(flow.Id) == "Cancelled" || Cancelled(gate.Parent));
        void Cancel(Gate gate)
        {
            foreach (var key in gate.States.Keys.ToList()) if (gate.States[key] == "Pending") gate.States[key] = "Cancelled";
            pending.RemoveAll(p => Cancelled(p.Flow));
            foreach (var child in gates.Where(g => Cancelled(g.Parent))) { child.Stopped = true; }
        }
        void ResolveFailures()
        {
            foreach (var gate in gates.Where(g => !g.Released && !g.Stopped).ToList())
            {
                var config = gate.Join.Routing!;
                if (!(config.DeadlineSeconds is int duration && clock - gate.At >= duration) && !RoutingEvaluator.JoinImpossible(config.WaitFor, gate.States.Values.ToList())) continue;
                gate.Stopped = true;
                trace.Add(new(gate.Join.Key, "Attention", "Join requirements failed or the deadline expired.", gate.Parent.Id));
                if (config.FailureTransitionKey is null) continue;
                gate.Released = true; Cancel(gate); Follow(gate.Join, gate.Parent, config.FailureTransitionKey);
            }
        }
        void Drain()
        {
            while (queue.TryDequeue(out var item))
            {
                if (++count > 2000) { trace.Add(new(item.Node, "Attention", "Simulation stopped at its execution limit; check automatic loops.")); queue.Clear(); return; }
                var node = nodes[item.Node]; var flow = item.Flow;
                if (Cancelled(flow)) continue;
                if (node.Type == "Start") { Follow(node, flow); continue; }
                if (node.Type == "End") { trace.Add(new(node.Key, "Completed", "This continuation reached End.", flow.Id)); continue; }
                if (node.Type == "ExclusiveGateway")
                {
                    var decision = RoutingEvaluator.Evaluate(node.Routing!, flow.Values);
                    trace.AddRange(decision.Rules.Select((r, index) => new RoutingTrace(node.Key, r.Status, r.Problem ?? r.TransitionKey, flow.Id, r, index + 1)));
                    trace.Add(new(node.Key, decision.Problem is null ? "Matched" : "Attention",
                        decision.Problem ?? (decision.UsedFallback ? "Fallback selected." : "Matched: " + string.Join(", ", decision.Matched)), flow.Id));
                    foreach (var key in decision.Transitions) Follow(node, flow, key);
                    if (decision.Problem is not null && flow.Gate is { } failedGate) { failedGate.States[flow.Id] = "Unmet"; ResolveFailures(); }
                }
                else if (RoutingGraphRules.IsFork(node.Type))
                {
                    var edges = graph.Connections.Where(e => e.From == node.Key).ToList();
                    var decision = node.Type == "InclusiveGateway" ? RoutingEvaluator.Evaluate(node.Routing!, flow.Values)
                        : new RoutingDecision(edges.Where(e => e.Key != node.Routing?.FallbackTransitionKey).Select(e => e.Key).ToList(), []);
                    if (decision.Problem is not null) { trace.Add(new(node.Key, "Attention", decision.Problem, flow.Id)); if (flow.Gate is { } failed) { failed.States[flow.Id] = "Unmet"; ResolveFailures(); } continue; }
                    if (decision.UsedFallback) { Follow(node, flow, decision.Transitions[0]); continue; }
                    RoutingData.ClearNodes(flow.Values, RoutingGraphRules.Region(graph, node.Key, node.Routing!.JoinNodeKey!).Append(node.Routing.JoinNodeKey!));
                    var gate = new Gate(node, nodes[node.Routing!.JoinNodeKey!], flow, (int)Math.Min(clock, int.MaxValue)); gates.Add(gate); cycle++;
                    foreach (var edge in edges)
                    {
                        if (!decision.Transitions.Contains(edge.Key)) { trace.Add(new(node.Key, "Skipped", "Branch was not activated: " + edge.Key, flow.Id)); continue; }
                        var branch = new Flow($"{flow.Id}/{node.Key}:{cycle}/{edge.Key}", new(flow.Values), gate);
                        gate.States[branch.Id] = "Pending"; gate.Branches[branch.Id] = branch;
                    }
                    foreach (var branch in gate.Branches.Values) Follow(node, branch, branch.Id.Split('/').Last());
                }
                else if (node.Type == "JoinGateway")
                {
                    var gate = flow.Gate;
                    if (gate is null || gate.Join.Key != node.Key) { trace.Add(new(node.Key, "Attention", "Branch reached the wrong Join.", flow.Id)); continue; }
                    gate.States[flow.Id] = "Met";
                    if (gate.Stopped) continue;
                    if (gate.Released) { trace.Add(new(node.Key, "Completed", "Late branch recorded; continuation was already released.", flow.Id)); continue; }
                    var config = node.Routing!;
                    if (!RoutingEvaluator.JoinReady(config.WaitFor, gate.States.Values.ToList())) continue;
                    gate.Released = true;
                    foreach (var result in config.WaitFor == "Any" ? new[] { flow } : gate.Branches.Values.ToArray())
                    { RoutingData.Merge(gate.Parent.Values, result.Values, result.Changed); gate.Parent.Changed.UnionWith(result.Changed); }
                    RoutingData.JoinOutput(gate.Parent.Values, node.Key, config.WaitFor == "Any" ? flow.Values : null, flow.Changed);
                    gate.Parent.Changed.Add(node.Key);
                    if (config.RemainingBranches == "Cancel")
                    {
                        Cancel(gate);
                    }
                    trace.Add(new(node.Key, "Released", "Requirements met; continuation released once.", gate.Parent.Id));
                    Follow(node, gate.Parent, graph.Connections.First(e => e.From == node.Key && e.Key != config.FailureTransitionKey).Key);
                }
                else { pending.Add((node, flow)); trace.Add(new(node.Key, "Waiting", "Provide a mocked completion.", flow.Id)); }
            }
        }
        Drain();
        foreach (var signal in input.Signals)
        {
            clock += Math.Clamp(signal.AdvanceSeconds, 0, 31536000);
            ResolveFailures();
            var waiting = pending.Where(p => p.Node.Key == signal.NodeKey && (signal.Branch is null || signal.Branch == p.Flow.Id)).ToList();
            if (waiting.Count != 1)
            { trace.Add(new(signal.NodeKey, "Ignored", waiting.Count == 0 ? "No active visit; duplicate or late signal ignored." : "Select a specific branch for this completion.")); Drain(); continue; }
            var current = waiting[0]; pending.Remove(current);
            RoutingData.ReplaceNode(current.Flow.Values, current.Node.Key, (signal.Values ?? []).ToDictionary(p => p.Key, p => (object?)p.Value), signal.Outcome);
            current.Flow.Changed.Add(current.Node.Key);
            trace.Add(new(current.Node.Key, "Completed", "Mock result captured for this visit.", current.Flow.Id));
            Follow(current.Node, current.Flow); Drain();
        }
        var waits = pending.Select(p => new RoutingTrace(p.Node.Key, "Waiting", "Waiting for a mocked completion.", p.Flow.Id)).ToList();
        waits.AddRange(gates.Where(g => !g.Released).Select(g => new RoutingTrace(g.Join.Key, g.Stopped ? "Attention" : "Waiting",
            $"{g.States.Values.Count(s => s == "Met")} of {g.States.Count} requirements met; waiting for {string.Join(", ", g.States.Where(p => p.Value == "Pending").Select(p => nodes.GetValueOrDefault(graph.Connections.FirstOrDefault(e => e.From == g.Fork.Key && e.Key == p.Key.Split('/').Last())?.To ?? "")?.Name ?? p.Key))}.", g.Parent.Id)));
        if (waits.Count > 0 && trace.Any(t => t.Status == "Completed" && nodes.GetValueOrDefault(t.NodeKey)?.Type == "End"))
            waits.Add(new(start.Key, "Waiting", "The continuation reached End; outstanding workflow work must finish before the run completes."));
        return new(trace, waits, []);
    }
}
