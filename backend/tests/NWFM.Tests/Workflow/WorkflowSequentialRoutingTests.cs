using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Workflow.Application.Integrations;
using Workflow.Application.Routing;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Modules.Workflow;

public sealed class WorkflowSequentialRoutingTests
{
    private static RoutingConfiguration Config(int version = 2) => new()
    {
        ContractVersion = version,
        MatchMode = "First",
        FallbackTransitionKey = "fallback",
        Sources = [new("$input.one", "$input", "one", "yes_no", "First"), new("$input.two", "$input", "two", "yes_no", "Second"), new("later.three", "later", "three", "yes_no", "Third")],
        Routes = new[] { "$input.one", "$input.two", "later.three" }.Select((key, i) => new RoutingRule
        { TransitionKey = "route" + (i + 1), When = new() { Conditions = [new() { Field = key, Operator = "equal", Value = JsonSerializer.SerializeToElement(true) }] } }).ToList()
    };
    private static Dictionary<string, object?> Values(bool one = false, bool two = false, bool three = false) => new()
    { ["$input.one"] = one, ["$input.two"] = two, ["later.three"] = three, ["later.$outcome"] = "success" };
    [Fact]
    public void FirstAndThirdTrue_OnlyFirstIsVisited()
    {
        var decision = RoutingEvaluator.Evaluate(Config(), Values(true, false, true));
        decision.Transitions.Should().Equal("route1"); decision.Matched.Should().Equal("route1");
        decision.Rules.Select(r => r.Status).Should().Equal("Matched", "Skipped", "Skipped");
    }
    [Fact]
    public void OnlyThirdTrue_VisitsInPersistedOrder_AndFallbackIsExplicit()
    {
        var result = RoutingEvaluator.Evaluate(Config(), Values(three: true));
        result.Rules.Select(r => r.Status).Should().Equal("Checked", "Checked", "Matched"); result.Transitions.Should().Equal("route3");
        result = RoutingEvaluator.Evaluate(Config(), Values()); result.UsedFallback.Should().BeTrue(); result.Transitions.Should().Equal("fallback");
    }
    [Fact]
    public void LaterUnavailableAndMalformedValuesAreNeverReadAfterMatch()
    {
        var values = Values(one: true); values.Remove("later.$outcome"); values["$input.two"] = "malformed";
        RoutingEvaluator.Evaluate(Config(), values).Problem.Should().BeNull();
        RoutingEvaluator.Evaluate(Config(1), values).Problem.Should().NotBeNull();
        values["later.$outcome"] = "success";
        RoutingEvaluator.Evaluate(Config(1), values).Problem.Should().NotBeNull();
    }
    [Fact]
    public void VisitedMissingIsNotNull_AndMalformedIsNeverFallback()
    {
        var values = Values(); values.Remove("$input.one");
        RoutingEvaluator.Evaluate(Config(), values).Problem.Should().Contain("Missing");
        values["$input.one"] = null;
        RoutingEvaluator.Evaluate(Config(), values).UsedFallback.Should().BeTrue();
        values["$input.one"] = "false";
        RoutingEvaluator.Evaluate(Config(), values).Problem.Should().Contain("type");
    }
    [Fact]
    public void AllReferencesInVisitedRuleAreValidatedEvenWhenGroupCouldShortCircuit()
    {
        var config = Config(); config.Routes[0].When.Match = "Any";
        config.Routes[0].When.Conditions.Add(config.Routes[2].When.Conditions[0]);
        var values = Values(one: true); values.Remove("later.three");
        RoutingEvaluator.Evaluate(config, values).Problem.Should().Contain("Missing");
    }
    [Fact]
    public void PublicationValidatesSkippedDefinition_RuntimeDoesNot()
    {
        var config = Config(); config.Routes[2].When.Conditions[0].Operator = "invalid";
        RoutingEvaluator.Validate(config).Should().NotBeEmpty();
        RoutingEvaluator.Evaluate(config, Values(one: true)).Problem.Should().BeNull();
        RoutingEvaluator.Evaluate(config, Values()).Problem.Should().NotBeNull();
        config.Routes[2] = null!; config.Sources[2] = null!;
        RoutingEvaluator.Validate(config).Should().NotBeEmpty();
        RoutingEvaluator.Evaluate(config, Values(one: true)).Problem.Should().BeNull();
    }
    [Fact]
    public void V1FirstStillEvaluatesAll_V1UniqueAndAllRemainUnchanged()
    {
        var config = Config(1); var values = Values(true, false, true);
        RoutingEvaluator.Evaluate(config, values).Matched.Should().Equal("route1", "route3");
        config.MatchMode = "Unique"; RoutingEvaluator.Evaluate(config, values).Problem.Should().Contain("More than one");
        config.MatchMode = "All"; RoutingEvaluator.Evaluate(config, values).Transitions.Should().Equal("route1", "route3");
    }
    [Fact]
    public void CompileRoundtripPreservesOrderStableKeysAndExplicitVersion()
    {
        var config = Config(); config.Routes.Reverse();
        XNamespace ns = "https://privora.io/workflow/v1";
        var xml = new XElement(ns + "Workflow", new XElement(ns + "Activities", new XElement(ns + "Activity",
            new XAttribute("nodeKey", "condition"), new XAttribute("name", "Condition"), new XAttribute("type", "ExclusiveGateway"),
            new XAttribute("configurationJson", JsonSerializer.Serialize(new { routing = config }, IntegrationJson.Options)))),
            new XElement(ns + "Transitions", new XElement(ns + "Transition", new XAttribute("key", "route3"), new XAttribute("from", "condition"), new XAttribute("to", "condition"), new XAttribute("priority", 7))));
        var compiled = new WorkflowXmlCompiler().Compile(xml.ToString(), out _); compiled.IsSuccess.Should().BeTrue();
        var reloaded = RoutingDefinition.Read(compiled.Value.Activities.Single().ConfigurationJson)!;
        reloaded.ContractVersion.Should().Be(2); reloaded.Routes.Select(r => r.TransitionKey).Should().Equal("route3", "route2", "route1");
        compiled.Value.Transitions.Single().Key.Should().Be("route3"); compiled.Value.Transitions.Single().Priority.Should().Be(7);
        RoutingEvaluator.Evaluate(reloaded, Values(true, false, true)).Transitions.Should().Equal("route3");
    }
    [Fact]
    public void SimulationExposesSamePerRuleTrace()
    {
        var config = Config(); config.Sources.RemoveAt(2); config.Routes.RemoveAt(2);
        var graph = new RoutingGraph([new("start", "Start", "Start"), new("condition", "Condition", "ExclusiveGateway", config), new("end", "End", "End")],
            [new("entry", "start", "condition"), new("route1", "condition", "end"), new("route2", "condition", "end"), new("fallback", "condition", "end")]);
        var result = RoutingSimulator.Run(graph, new([], new() { ["one"] = JsonSerializer.SerializeToElement(true) }));
        result.Diagnostics.Should().BeEmpty(); result.Trace.Should().Contain(t => t.Status == "Matched" && t.Explanation == "route1");
        result.Trace.Should().Contain(t => t.Status == "Skipped" && t.Explanation == "route2");
        result.Trace.Where(t => t.Rule is not null).Select(t => t.Rule!.TransitionKey).Should().Equal("route1", "route2");
        result.Trace.Where(t => t.Rule is not null).Select(t => t.RuleOrder).Should().Equal(1, 2);
        result.Trace.Where(t => t.Rule is not null).Should().OnlyContain(t => t.Rule!.Status == t.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimulationStructuredRulesKeepIdentityForCheckedMatchedSkippedAndErrors(bool error)
    {
        var config = Config();
        var graph = new RoutingGraph([new("start", "Start", "Start"), new("condition", "Condition", "ExclusiveGateway", config), new("end", "End", "End")],
            [new("entry", "start", "condition"), new("route1", "condition", "end"), new("route2", "condition", "end"), new("route3", "condition", "end"), new("fallback", "condition", "end")]);
        var inputs = new Dictionary<string, JsonElement> { ["one"] = JsonSerializer.SerializeToElement(false) };
        if (!error) inputs["two"] = JsonSerializer.SerializeToElement(true);
        var result = RoutingSimulator.Run(graph, new([], inputs));
        var rules = result.Trace.Where(t => t.Rule is not null).ToList();
        rules.Select(t => t.Rule!.TransitionKey).Should().Equal("route1", "route2", "route3");
        rules.Select(t => t.RuleOrder).Should().Equal(1, 2, 3);
        rules.Select(t => t.Rule!.Status).Should().Equal("Checked", error ? "Checked" : "Matched", "Skipped");
        rules[1].Rule!.Problem.Should().Be(error ? rules[1].Explanation : null);
        if (error) { rules[1].Rule!.Problem.Should().Contain("Missing"); result.Trace.Should().Contain(t => t.Status == "Attention" && t.Explanation == rules[1].Rule!.Problem); }
        var json = JsonSerializer.SerializeToElement(rules[1], IntegrationJson.Options);
        json.GetProperty("rule").GetProperty("transitionKey").GetString().Should().Be("route2");
        json.GetProperty("ruleOrder").GetInt32().Should().Be(2);
        json.GetProperty("explanation").GetString().Should().Be(rules[1].Explanation);
    }
}
