using System.Text.Json;
using NWFM.Shared.Integration.Forms;
using Workflow.Application.Workspace;

namespace Workflow.Application.Routing;

public sealed class RoutingConfiguration
{
    public int ContractVersion { get; set; } = 1;
    public string MatchMode { get; set; } = "Unique";
    public List<RoutingRule> Routes { get; set; } = [];
    public List<RoutingSource> Sources { get; set; } = [];
    public string? FallbackTransitionKey { get; set; }
    public string? JoinNodeKey { get; set; }
    public string WaitFor { get; set; } = "All";
    public string RemainingBranches { get; set; } = "Continue";
    public int? DeadlineSeconds { get; set; }
    public string? FailureTransitionKey { get; set; }
}

public sealed class RoutingRule
{
    public string TransitionKey { get; set; } = "";
    public bool Always { get; set; }
    public FormConditionGroup When { get; set; } = new();
}

public sealed record RoutingSource(string Key, string NodeKey, string Field, string Type,
    string Label, Guid? FormId = null, int? FormVersion = null, bool Sensitive = false,
    IReadOnlyList<FormChoiceInfo>? Choices = null)
{
    public FormFieldInfo AsField() => new(Key, Type, Label, Label, null, Choices ?? []);
}

public sealed record RoutingOutput(string Key, string Type, string Label, bool Sensitive = false,
    IReadOnlyList<FormChoiceInfo>? Choices = null);
public sealed record RoutingDecision(IReadOnlyList<string> Transitions, IReadOnlyList<string> Matched,
    string? Problem = null, bool UsedFallback = false)
{
    public IReadOnlyList<RoutingRuleTrace> Rules { get; init; } = [];
}
public sealed record RoutingRuleTrace(string TransitionKey, string Status, string? Problem = null);
public sealed record RoutingNode(string Key, string Name, string Type, RoutingConfiguration? Routing = null);
public sealed record RoutingConnection(string Key, string From, string To);
public sealed record RoutingGraph(IReadOnlyList<RoutingNode> Nodes, IReadOnlyList<RoutingConnection> Connections);
public sealed record RoutingDiagnostic(string Code, string Message, string? NodeKey = null, string? Field = null, string? ConnectionKey = null);
public sealed record RoutingContext(IReadOnlyList<RoutingSource> Sources, IReadOnlyList<RoutingNode> Nodes,
    IReadOnlyList<RoutingConnection> Connections, bool Enabled, string Revision);
public sealed record RoutingSimulationSignal(string NodeKey, Dictionary<string, JsonElement>? Values = null,
    string Outcome = "success", int AdvanceSeconds = 0, string? Branch = null);
public sealed record RoutingSimulationInput(IReadOnlyList<RoutingSimulationSignal> Signals,
    Dictionary<string, JsonElement>? Inputs = null);
public sealed record RoutingTrace(string NodeKey, string Status, string Explanation, string? Branch = null,
    RoutingRuleTrace? Rule = null, int? RuleOrder = null);
public sealed record RoutingSimulationResult(IReadOnlyList<RoutingTrace> Trace, IReadOnlyList<RoutingTrace> Waiting,
    IReadOnlyList<RoutingDiagnostic> Diagnostics);
