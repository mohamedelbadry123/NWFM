namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Routing;

public sealed class WorkflowRoutingTests
{
    [Fact]
    public void InvalidDraftStructure_ProducesDiagnosticsRatherThanThrowing()
    {
        var graph = Parallel();
        graph.Nodes.Single(n => n.Key == "oneCondition").Routing!.Routes = null!;
        RoutingGraphRules.Validate(graph).Should().Contain(d => d.Code == "ROUTING_RULE");
        var config = Rule("one", "approved", "yes_no", "equal", true);
        config.Routes[0].When.Conditions = null!;
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?>()).Problem.Should().NotBeNull();
    }
    internal static RoutingGraph Parallel(string mode = "All", string remaining = "Continue", int? deadline = null)
    {
        var nodes = new List<RoutingNode> { new("start", "Start", "Start"), new("fork", "Fork", "ParallelGateway", new() { JoinNodeKey = "join", MatchMode = "All" }),
            new("join", "Join", "JoinGateway", new() { WaitFor = mode, RemainingBranches = remaining, DeadlineSeconds = deadline }), new("next", "Step 3", "UserTask"), new("finishCondition", "Finish", "ExclusiveGateway", new() { Routes = [new() { Always = true, TransitionKey = "finish" }] }), new("end", "End", "End") };
        var edges = new List<RoutingConnection> { new("entry", "start", "fork"), new("continue", "join", "next"), new("finishCheck", "next", "finishCondition"), new("finish", "finishCondition", "end") };
        foreach (var key in new[] { "one", "two", "event16" })
        {
            nodes.Add(new(key, key, key == "event16" ? "WaitEvent" : "UserTask"));
            nodes.Add(new(key + "Condition", "Qualified", "ExclusiveGateway", Rule(key, "approved", "yes_no", "equal", true, key + "_met")));
            edges.Add(new("fork_" + key, "fork", key)); edges.Add(new(key + "_check", key, key + "Condition")); edges.Add(new(key + "_met", key + "Condition", "join"));
        }
        return new(nodes, edges);
    }
    internal static RoutingConfiguration Rule(string node, string field, string type, string op, object? value, string transition = "yes") => new()
    {
        Sources = [new(node + "." + field, node, field, type, field)],
        Routes = [new() { TransitionKey = transition, When = new() { Conditions = [new() { Field = node + "." + field, Operator = op, Value = JsonSerializer.SerializeToElement(value) }] } }]
    };
    internal static RoutingSimulationSignal Signal(string node, bool approved = true) => new(node, new() { ["approved"] = JsonSerializer.SerializeToElement(approved) });

    [Theory]
    [InlineData("one,two,event16")]
    [InlineData("one,event16,two")]
    [InlineData("two,one,event16")]
    [InlineData("two,event16,one")]
    [InlineData("event16,one,two")]
    [InlineData("event16,two,one")]
    public void All_RemembersEveryArrival_AndReleasesOnce(string order)
    {
        var signals = order.Split(',').Select(n => Signal(n)).ToList();
        var partial = RoutingSimulator.Run(Parallel(), new(signals.Take(2).ToList()));
        partial.Waiting.Should().Contain(w => w.NodeKey == "join" && w.Explanation.StartsWith("2 of 3"));
        var complete = RoutingSimulator.Run(Parallel(), new(signals.Concat(signals).ToList()));
        complete.Diagnostics.Should().BeEmpty();
        complete.Trace.Count(t => t.Status == "Released").Should().Be(1);
        complete.Waiting.Should().ContainSingle(w => w.NodeKey == "next");
    }
    [Theory]
    [InlineData("Continue", 3)]
    [InlineData("Cancel", 1)]
    public void Any_ReleasesOnce_AndAppliesRemainingPolicy(string policy, int waiting)
    {
        var result = RoutingSimulator.Run(Parallel("Any", policy), new([Signal("one")]));
        result.Trace.Count(t => t.Status == "Released").Should().Be(1);
        result.Waiting.Count.Should().Be(waiting);
        result = RoutingSimulator.Run(Parallel("Any", policy), new([Signal("one"), Signal("two"), Signal("event16")]));
        result.Trace.Count(t => t.Status == "Released").Should().Be(1);
    }
    [Fact]
    public void FailedQualification_MakesAllImpossibleImmediately()
    {
        var result = RoutingSimulator.Run(Parallel(), new([Signal("one", false)]));
        result.Waiting.Should().Contain(w => w.NodeKey == "join" && w.Status == "Attention");
        result.Trace.Should().NotContain(t => t.Status == "Released");
    }
    [Fact]
    public void Deadline_DoesNotCountAsSuccess()
    {
        var result = RoutingSimulator.Run(Parallel(deadline: 5), new([Signal("one"), new("$clock", AdvanceSeconds: 6), Signal("two"), Signal("event16")]));
        result.Trace.Should().NotContain(t => t.Status == "Released");
        result.Waiting.Should().Contain(w => w.NodeKey == "join" && w.Status == "Attention");
    }
    [Fact]
    public void UniqueConflicts_Stop_AndPriorityIsExplicit()
    {
        var config = new RoutingConfiguration { Routes = [new() { Always = true, TransitionKey = "a" }, new() { Always = true, TransitionKey = "b" }] };
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?>()).Problem.Should().Contain("More than one");
        config.MatchMode = "First";
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?>()).Transitions.Should().Equal("a");
        config.MatchMode = "All";
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?>()).Transitions.Should().Equal("a", "b");
    }
    [Fact]
    public void UnavailableSource_IsDifferentFromEmptySubmittedField()
    {
        var config = Rule("registration", "name", "text", "is_empty", null);
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?>()).Problem.Should().NotBeNull();
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?> { ["registration.$outcome"] = "success" }).Transitions.Should().Equal("yes");
    }
    [Fact]
    public void MalformedTypedValue_CannotBecomeFallback()
    {
        var config = Rule("one", "amount", "numeric", "greater_than", 20); config.FallbackTransitionKey = "fallback";
        var result = RoutingEvaluator.Evaluate(config, new Dictionary<string, object?> { ["one.$outcome"] = "success", ["one.amount"] = "not a number" });
        result.Problem.Should().NotBeNull(); result.Transitions.Should().BeEmpty();
    }
    [Fact]
    public void BranchMerge_CannotOverwriteAnotherResultWithItsInheritedSnapshot()
    {
        var target = new Dictionary<string, object?> { ["one.amount"] = 100, ["two.old"] = "stale", ["$input.reference"] = "R1" };
        var branch = new Dictionary<string, object?> { ["one.amount"] = 0, ["two.amount"] = 200, ["two.$outcome"] = "success" };
        RoutingData.Merge(target, branch, ["two"]);
        target["one.amount"].Should().Be(100); target["two.amount"].Should().Be(200); target.Should().NotContainKey("two.old");
    }
    [Fact]
    public void Graph_RejectsExternalBranchEntry_AndJoinBypass()
    {
        var graph = Parallel();
        var changed = graph with { Connections = graph.Connections.Concat([new RoutingConnection("cross", "two", "one")]).ToList() };
        RoutingGraphRules.Validate(changed).Should().Contain(e => e.Code == "ROUTING_BRANCH_CROSSING" || e.Code == "ROUTING_BRANCH_ENTRY");
        changed = graph with { Connections = graph.Connections.Select(e => e.Key == "one_met" ? e with { To = "end" } : e).ToList() };
        RoutingGraphRules.Validate(changed).Should().Contain(e => e.Code == "ROUTING_JOIN_BYPASS");
    }
    [Fact]
    public void MatchingFork_CountsOnlyActivatedBranches()
    {
        var graph = Parallel();
        var config = new RoutingConfiguration { MatchMode = "All", JoinNodeKey = "join", Routes = [new() { Always = true, TransitionKey = "fork_one" }, new() { Always = true, TransitionKey = "fork_two" }, new() { TransitionKey = "fork_event16", When = Rule("$input", "runEvent", "yes_no", "equal", true).Routes[0].When }], Sources = [new("$input.runEvent", "$input", "runEvent", "yes_no", "Run event")] };
        graph = graph with { Nodes = graph.Nodes.Select(n => n.Key == "fork" ? n with { Type = "InclusiveGateway", Routing = config } : n).ToList() };
        var result = RoutingSimulator.Run(graph, new([Signal("one"), Signal("two")], new() { ["runEvent"] = JsonSerializer.SerializeToElement(false) }));
        result.Diagnostics.Should().BeEmpty(); result.Trace.Should().Contain(t => t.Status == "Skipped"); result.Waiting.Should().ContainSingle(w => w.NodeKey == "next");
    }
    [Fact]
    public void CorrectionLoop_StartsAFreshForkCycle()
    {
        var graph = Parallel();
        graph = graph with { Connections = graph.Connections.Select(e => e.Key == "finish" ? e with { To = "fork" } : e).ToList() };
        var result = RoutingSimulator.Run(graph, new([Signal("one"), Signal("two"), Signal("event16"), new("next"), Signal("one")]));
        result.Diagnostics.Should().BeEmpty();
        result.Trace.Count(t => t.Status == "Released").Should().Be(1);
        result.Waiting.Should().Contain(w => w.NodeKey == "join" && w.Explanation.StartsWith("1 of 3"));
    }
    [Fact]
    public void NestedFork_HasIndependentJoinActivation()
    {
        var graph = Parallel();
        var nodes = graph.Nodes.Concat(new RoutingNode[] {
            new("inner", "Nested fork", "ParallelGateway", new() { JoinNodeKey="innerJoin", MatchMode="All" }),
            new("extra", "Extra event", "WaitEvent"),new("extraCheck", "Extra condition", "ExclusiveGateway", new() { Routes=[new(){Always=true,TransitionKey="extraMet"}] }),
            new("innerJoin", "Nested join", "JoinGateway", new()),new("outerCheck","Nested group complete","ExclusiveGateway",new(){Routes=[new(){Always=true,TransitionKey="outerMet"}]}) }).ToList();
        var edges = graph.Connections.Select(e => e.Key == "fork_one" ? e with { To = "inner" } : e.Key == "one_met" ? e with { To = "innerJoin" } : e).Concat(new RoutingConnection[]{
            new("innerOne","inner","one"),new("innerExtra","inner","extra"),new("extraExit","extra","extraCheck"),new("extraMet","extraCheck","innerJoin"),new("innerDone","innerJoin","outerCheck"),new("outerMet","outerCheck","join")}).ToList();
        graph = new(nodes, edges);
        var result = RoutingSimulator.Run(graph, new([Signal("two"), Signal("event16"), Signal("one")]));
        result.Diagnostics.Should().BeEmpty(); result.Waiting.Should().Contain(w => w.NodeKey == "innerJoin" && w.Explanation.StartsWith("1 of 2"));
        result.Trace.Should().NotContain(t => t.Status == "Released");
        result = RoutingSimulator.Run(graph, new([Signal("two"), Signal("event16"), Signal("one"), new("extra")]));
        result.Trace.Count(t => t.Status == "Released").Should().Be(2);
        result.Waiting.Should().ContainSingle(w => w.NodeKey == "next");
    }
    [Fact]
    public void Any_KeepRunning_WaitsForOutstandingWorkAtEnd()
    {
        var result = RoutingSimulator.Run(Parallel("Any"), new([Signal("one"), new("next")]));
        result.Waiting.Should().Contain(w => w.Explanation.Contains("outstanding workflow work"));
        result = RoutingSimulator.Run(Parallel("Any"), new([Signal("one"), new("next"), Signal("two"), Signal("event16")]));
        result.Waiting.Should().BeEmpty(); result.Trace.Count(t => t.Status == "Released").Should().Be(1);
    }
}
