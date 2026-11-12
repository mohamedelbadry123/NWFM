using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;

namespace NWFM.Tests.Modules.Workflow;

public sealed class DesignerMappingTests
{
    [Theory]
    [InlineData(false, "equal", true)]
    [InlineData(true, "equal", true)]
    [InlineData(false, "greater", false)]
    [InlineData(true, "greater", false)]
    public void NumericComparisonIsSymmetricAcrossNumberAndInteger(bool reverse, string op, bool expected)
    {
        var number = new TransformNode("literal", J(2.0m), LiteralSchema: new("number"));
        var integer = L(2); var expression = new TransformNode(op, Args: reverse ? [integer, number] : [number, integer]);
        var config = new DesignerMappingConfiguration(1, new("POST", "/", [], new("boolean", Required: true)), [new("body", "$body", expression)], [], [], [], []);
        var preview = MappingRequestPreparation.Preview(new(config, []), Catalog(), Owner);
        preview.Errors.Should().BeEmpty(); preview.Request!.Body.GetBoolean().Should().Be(expected);
    }
    [Fact]
    public void CompletedCallerDataIsNotAnInvocationCycleButRecursiveCallsAndPrerequisitesAre()
    {
        var caller = new CallableTarget(Definition, Version, "caller");
        var output = new SourceReference(Definition, Version, "caller", "EventOutput", "total");
        var schema = new MappingSchema("number");
        var catalog = new MappingCatalog([new(output, "Total", [], schema, "CompletedOutput")],
            [new(caller, "Caller", [], schema, true), new(Owner, "Callee", [], schema, true)], [Owner, caller], [new(caller, Owner, "Invocation")]);
        var config = new DesignerMappingConfiguration(1, new("POST", "/", [], schema), [new("body", "$body", S(output))], [], [], [], [], true, schema);
        MappingValidation.Validate(config, catalog, Owner).Should().BeEmpty();
        MappingValidation.Validate(config with { Actions = [new("back", caller, "Run", L(1))] }, catalog, Owner).Should().Contain(e => e.Contains("cycle"));
        MappingValidation.Validate(config, catalog with { Dependencies = [new(caller, Owner, "Source")] }, Owner).Should().Contain(e => e.Contains("cycle"));
        MappingValidation.Validate(config with { Actions = [new("prepare", caller, "MapOnly", L(1))] }, catalog, Owner).Should().BeEmpty();
    }
    private static readonly Guid Definition = Guid.NewGuid(), Version = Guid.NewGuid();
    private static readonly CallableTarget Owner = new(Definition, Version, "send");
    private static readonly SourceReference Name = new(Definition, Version, "step1", "FormField", "person.name", FormStepId: "first", FormId: Guid.NewGuid(), FormVersion: 1);
    private static readonly SourceReference Amount = new(Definition, Version, "step2", "FormField", "amount", FormStepId: "second", FormId: Guid.NewGuid(), FormVersion: 4);
    private static readonly SourceReference Items = new(Definition, Version, "event", "EventOutput", "items");
    private static JsonElement J(object? value) => JsonSerializer.SerializeToElement(value);
    private static TransformNode L(object? value) => new("literal", J(value));
    private static TransformNode S(SourceReference reference) => new("source", Source: reference);
    private static MappingCatalog Catalog() => new([
        new(Name, "Name", ["Main", "Step 1", "Form 1", "Name"], new("string"), "SubmittedForm"),
        new(Amount, "Amount", ["Main", "Step 2", "Form 2", "Amount"], new("number"), "SubmittedForm"),
        new(Items, "Items", ["Main", "Event", "Items"], new("array", Items: new("object", Properties: new() { ["cost"] = new("number", Required: true) })), "CompletedOutput")],
        [new(Owner, "Send", [], new("object", Properties: []), false)], [Owner, new(Definition, Version, "step1"), new(Definition, Version, "step2"), new(Definition, Version, "event")],
        new[] { "step1", "step2", "event" }.Select(n => new MappingDependency(new(Definition, Version, n), Owner, "Graph")).ToList());
    private static DesignerMappingConfiguration Config() => new(1,
        new("POST", "/people/{id}", [new("path", "id", new("string", Required: true)), new("query", "search", new("string")), new("header", "X-Label", new("string"))],
            new("object", Required: true, Properties: new() { ["literal.key"] = new("string", Required: true), ["total"] = new("number", Required: true), ["items"] = Catalog().Sources[2].Schema with { Required = true } })),
        [new("path", "id", L("a b")), new("query", "search", L("x&admin=true")), new("header", "X-Label", L("{{untrusted}}")),
            new("body", "$body", new("object", Properties: new() { ["literal.key"] = S(Name), ["total"] = new("add", Args: [S(Amount), L(2)]), ["items"] = S(Items) }))], [], [], [], []);
    private static List<MappingSample> Samples() => [new(Name, J("Ada")), new(Amount, J(12.5m)), new(Items, J(new[] { new { cost = 2 }, new { cost = 4 } }))];
    [Fact]
    public void MultiformAndEventRequestPreservesTypesKeysAndEncoding()
    {
        var preview = MappingRequestPreparation.Preview(new(Config(), Samples()), Catalog(), Owner);
        preview.Errors.Should().BeEmpty(); var request = preview.Request!;
        request.Path.Should().Be("/people/a%20b"); request.Query["search"].Should().Be("x&admin=true");
        request.Headers["X-Label"].Should().Be("{{untrusted}}"); request.Body.GetProperty("literal.key").GetString().Should().Be("Ada");
        request.Body.GetProperty("total").GetDecimal().Should().Be(14.5m); request.Body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }
    [Fact]
    public void ArrayMapFilterSumAndCountUseBoundedTypedOperations()
    {
        var filtered = new TransformNode("filter", Args: [S(Items), new("greater", Args: [new("item", JsonPointer: "/cost"), L(2)])]);
        var mapped = new TransformNode("map", Args: [filtered, new("multiply", Args: [new("item", JsonPointer: "/cost"), L(3)])]);
        MappingEvaluator.Evaluate(new("sum", Args: [mapped]), Samples()).GetDecimal().Should().Be(12);
        MappingEvaluator.Evaluate(new("count", Args: [filtered]), Samples()).GetInt32().Should().Be(1);
    }
    [Fact]
    public void MissingNullFallbackAndCoalesceAreDistinct()
    {
        var reference = S(Name); Action missing = () => MappingEvaluator.Evaluate(reference, []); missing.Should().Throw<FormatException>().WithMessage("*missing*");
        MappingEvaluator.Evaluate(reference, [new(Name, J(null))]).ValueKind.Should().Be(JsonValueKind.Null);
        MappingEvaluator.Evaluate(reference with { Missing = new("Fallback", Fallback: J("safe")) }, []).GetString().Should().Be("safe");
        MappingEvaluator.Evaluate(new("coalesce", Args: [reference, L("backup")]), []).GetString().Should().Be("backup");
        Action invalid = () => MappingEvaluator.Evaluate(new("coalesce", Args: [new("number", Args: [L("bad")]), L(1)]), []);
        invalid.Should().Throw<FormatException>();
    }
    [Theory]
    [InlineData("number", "no")]
    [InlineData("boolean", "1")]
    [InlineData("boolean", "TRUE")]
    public void InvalidCastsFail(string op, string value)
    { Action act = () => MappingEvaluator.Evaluate(new(op, Args: [L(value)]), []); act.Should().Throw<FormatException>(); }
    [Fact]
    public void ArithmeticIsStrictInvariantAndCannotSilentlyOverflow()
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); MappingEvaluator.Evaluate(new("text", Args: [L(12.5m)]), []).GetString().Should().Be("12.5"); }
        finally { CultureInfo.CurrentCulture = previous; }
        foreach (var node in new[] { new TransformNode("divide", Args: [L(1), L(0)]), new("multiply", Args: [L(decimal.MaxValue), L(2)]), new("add", Args: [L("1"), L(2)]) })
        { Action act = () => MappingEvaluator.Evaluate(node, []); act.Should().Throw<FormatException>(); }
    }
    [Fact]
    public void SourcePinAndTypeErrorsAreNotLegacyFallbacks()
    {
        var config = Config(); config.Inputs[0] = new("path", "id", S(Name with { FormVersion = 9 }));
        MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("pin"));
        config.Inputs[0] = new("path", "id", S(Amount));
        MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("incompatible"));
        var unknown = Catalog() with { Sources = [Catalog().Sources[0] with { Schema = new("unknown"), Availability = "SchemaRequired" }, .. Catalog().Sources.Skip(1)] };
        config.Inputs[0] = new("path", "id", new("text", Args: [S(Name)]));
        MappingValidation.Validate(config, unknown, Owner).Should().Contain(e => e.Contains("typed schema"));
        var http = new HttpActivityConfiguration { ConnectionId = Guid.NewGuid(), MappingMode = "Typed", TypedMapping = config };
        http.MappingMode = "Legacy";
        IntegrationConfigurationRules.Validate("Http", JsonSerializer.Serialize(http, IntegrationJson.Options)).Should().Contain(e => e.Contains("cannot run in Legacy"));
    }
    [Fact]
    public void RequiredDuplicateAndUnknownDestinationsFail()
    {
        var config = Config(); config.Inputs.RemoveAt(0); MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("Required"));
        config = Config(); config.Inputs.Add(config.Inputs[0]); MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("Duplicate"));
        config = Config(); config.Inputs[0] = config.Inputs[0] with { Name = "unknown" }; MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("Unknown"));
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("%2fadmin")]
    [InlineData("x?y=1")]
    [InlineData("x\r\ny")]
    public void UnsafePathValuesFail(string value)
    { var c = Config(); c.Inputs[0] = c.Inputs[0] with { Value = L(value) }; MappingRequestPreparation.Preview(new(c, Samples()), Catalog(), Owner).Errors.Should().NotBeEmpty(); }
    [Theory]
    [InlineData("Host")]
    [InlineData("authorization")]
    [InlineData("Transfer-Encoding")]
    [InlineData("X-Bad\r\n")]
    public void ForbiddenHeadersFail(string name)
    { Action act = () => MappingValidation.Header(name, "value"); act.Should().Throw<FormatException>(); }
    [Fact]
    public void HeaderInjectionAndAbsolutePathsFail()
    {
        Action header = () => MappingValidation.Header("X-Test", "value\r\nX-Other: yes"); header.Should().Throw<FormatException>();
        foreach (var path in new[] { "//evil.invalid/", "https://evil.invalid/", "/%252e%252e/admin", "/a/../admin", "/a\\b" })
        { Action act = () => MappingValidation.Path(path, true); act.Should().Throw<FormatException>(); }
    }
    [Fact]
    public void TransformAndOutputLimitsFail()
    {
        var node = L("x"); for (var i = 0; i < 18; i++) node = new("concat", Args: [node]);
        Action depth = () => MappingEvaluator.Evaluate(node, []); depth.Should().Throw<FormatException>();
        Action array = () => MappingEvaluator.Evaluate(L(Enumerable.Range(0, 1001).ToArray()), []); array.Should().Throw<FormatException>();
        Action bytes = () => MappingEvaluator.Evaluate(L(new string('a', 262145)), []); bytes.Should().Throw<FormatException>();
        Action code = () => MappingEvaluator.Evaluate(new("eval", J("return 1")), []); code.Should().Throw<FormatException>();
    }
    [Fact]
    public void JsonPointerDoesNotSplitFieldIdentity_AndObjectKeysAreLiteral()
    {
        MappingEvaluator.Evaluate(S(Name), Samples()).GetString().Should().Be("Ada");
        var obj = new TransformNode("object", Properties: new() { ["a.b"] = L(1), ["slash/key"] = L(true) });
        var value = MappingEvaluator.Evaluate(obj, []); MappingTypes.Select(value, "/a.b").GetInt32().Should().Be(1); MappingTypes.Select(value, "/slash~1key").GetBoolean().Should().BeTrue();
    }
    [Fact]
    public void ContractsRoundtripMissingDefaultsAndTypedEmptyArrays()
    {
        var config = Config(); var json = JsonSerializer.Serialize(config, IntegrationJson.Options);
        var reload = JsonSerializer.Deserialize<DesignerMappingConfiguration>(json, IntegrationJson.Options)!;
        reload.Endpoint!.Parameters[0].Schema.Default.ValueKind.Should().Be(JsonValueKind.Undefined);
        MappingRequestPreparation.Preview(new(reload, Samples()), Catalog(), Owner).Errors.Should().BeEmpty();
        MappingEvaluator.Evaluate(new("literal", J(Array.Empty<string>()), LiteralSchema: new("array", Items: new("string"))), []).GetArrayLength().Should().Be(0);
    }
    [Fact]
    public void CompletionPreviewEvaluatesOutputsAndStableActionsWithoutDispatch()
    {
        var target = new CallableTarget(Definition, Version, "receiver");
        var catalog = Catalog() with { Targets = [.. Catalog().Targets, new(target, "Receiver", [], new("number"), true)] };
        var response = new SourceReference(Definition, Version, "send", "Response", "$response");
        var config = Config() with { Endpoint = Config().Endpoint! with { Response = new("object", Properties: new() { ["amount"] = new("number", Required: true) }) } };
        config.Outputs.Add(new("out-stable", "Total", new("number"), S(response with { JsonPointer = "/amount" })));
        config.Actions.Add(new("action-stable", target, "MapOnly", S(new(Definition, Version, "send", "EventOutput", "out-stable"))));
        var preview = MappingRequestPreparation.Preview(new(config, [new(response, J(new { amount = 7 }))], Owner, "Completion"), catalog, Owner);
        preview.Errors.Should().BeEmpty(); preview.Request.Should().BeNull(); preview.Outputs!["out-stable"].GetInt32().Should().Be(7);
        var action = preview.Actions!.Single(); action.ActionId.Should().Be("action-stable"); action.Mode.Should().Be("MapOnly"); action.Target.Should().Be(target); action.Inputs.GetInt32().Should().Be(7);
    }
    [Fact]
    public void UnknownTargetsInvocationCyclesAndForwardOutputReferencesFail()
    {
        var config = Config(); var target = new CallableTarget(Definition, Version, "receiver");
        config.Actions.Add(new("stable", target, "Run", L(1)));
        MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("callable"));
        var catalog = Catalog() with { Targets = [.. Catalog().Targets, new(target, "Receiver", [], new("number"), true)], Dependencies = [.. Catalog().Dependencies!, new(target, Owner, "Invocation")] };
        MappingValidation.Validate(config, catalog, Owner).Should().Contain(e => e.Contains("cycle"));
        config = Config(); config.Outputs.Add(new("self", "Self", new("number"), S(new(Definition, Version, "send", "EventOutput", "self"))));
        MappingValidation.Validate(config, Catalog(), Owner).Should().Contain(e => e.Contains("forward"));
    }
    [Fact]
    public void RelatedSelectorsRequirePinnedEqualityBoundedWindowsAndMatchingSource()
    {
        var otherDefinition = Guid.NewGuid(); var otherVersion = Guid.NewGuid();
        var related = Name with { WorkflowDefinitionId = otherDefinition, WorkflowVersionId = otherVersion };
        var catalog = Catalog() with { Sources = [.. Catalog().Sources, new(related, "Related", [], new("string"), "SubmittedForm")] };
        var config = Config(); config.Inputs[0] = new("path", "id", S(related));
        MappingValidation.Validate(config, catalog, Owner).Should().Contain(e => e.Contains("selector"));
        config.Selectors.Add(new("related", otherDefinition, otherVersion, "BusinessKey", L("customer-42"), WindowSeconds: 3600));
        config.Inputs[0] = new("path", "id", S(related with { SelectorId = "related" }));
        MappingValidation.Validate(config, catalog, Owner).Should().BeEmpty();
        config.Selectors[0] = config.Selectors[0] with { MultipleMatches = "Any" };
        MappingValidation.Validate(config, catalog, Owner).Should().NotBeEmpty();
        config.Selectors[0] = config.Selectors[0] with { MultipleMatches = "Latest", WindowSeconds = -1 };
        MappingValidation.Validate(config, catalog, Owner).Should().NotBeEmpty();
    }
    [Fact]
    public void InvalidWaitAndFallbackPoliciesFailAndNullableLiteralDefaultsWork()
    {
        Action wait = () => MappingValidation.Policy(new("Wait", DeadlineSeconds: 0)); wait.Should().Throw<FormatException>();
        var config = Config(); config.Inputs[0] = new("path", "id", S(Name) with { Missing = new("Fallback", Fallback: J(false)) });
        MappingValidation.Validate(config, Catalog(), Owner).Should().NotBeEmpty();
        var schema = new MappingSchema("object", Properties: new() { ["optional"] = new("string", Nullable: true), ["defaulted"] = new("number", Required: true, Default: J(5)) });
        var value = MappingRequestPreparation.EvaluateTyped(new("object", Properties: new() { ["optional"] = L(null) }), schema, []);
        value.GetProperty("optional").ValueKind.Should().Be(JsonValueKind.Null); value.GetProperty("defaulted").GetInt32().Should().Be(5);
    }
    [Fact]
    public void SelectorPreparationFreezesEqualityWindowAndStarterSeparately()
    {
        var anchor = DateTimeOffset.Parse("2026-11-10T10:00:00Z", CultureInfo.InvariantCulture); var starter = Guid.NewGuid();
        var selector = new RelatedInstanceSelector("lookup", Definition, Version, "BusinessKey", L("customer-42"), L(starter.ToString()), "Completed", 3600, Missing: new("Wait", 600, "expired"));
        var frozen = MappingSelectorPreparation.Freeze(selector, [], anchor);
        frozen.KeyValue.Should().Be("customer-42"); frozen.StartedByUserId.Should().Be(starter); frozen.MultipleMatches.Should().Be("Attention");
        frozen.WindowStart.Should().Be(anchor.AddHours(-1)); frozen.Deadline.Should().Be(anchor.AddMinutes(10));
        Action invalid = () => MappingSelectorPreparation.Freeze(selector with { StartedByUserId = L("customer-42") }, [], anchor); invalid.Should().Throw<FormatException>();
    }
    [Fact]
    public void StepOnlyPreviewChecksPinnedDestinationAndReadOnlyMode()
    {
        var destination = new SourceReference(Definition, Version, "send", "FormField", "prefill", FormStepId: "dest", FormId: Guid.NewGuid(), FormVersion: 2);
        var catalog = Catalog() with { Sources = [.. Catalog().Sources, new(destination, "Prefill", [], new("string", Nullable: true), "SubmittedForm")] };
        var config = new DesignerMappingConfiguration(1, null, [], [], [], [new("binding", "dest", destination.FormId!.Value, 2, "prefill", "ReadOnly", new("string"), S(Name))], []);
        var preview = MappingRequestPreparation.Preview(new(config, Samples(), Owner, "Step"), catalog, Owner);
        preview.Errors.Should().BeEmpty(); preview.StepValues!["binding"].GetString().Should().Be("Ada"); preview.Request.Should().BeNull();
        config.StepBindings[0] = config.StepBindings[0] with { FormVersion = 3 };
        MappingValidation.Validate(config, catalog, Owner).Should().Contain(e => e.Contains("pinned Step"));
    }
    [Fact]
    public void IntegerCastsAreExactAndLiteralIntegersMapToIntegerParameters()
    {
        var config = new DesignerMappingConfiguration(1, new("GET", "/", [new("query", "page", new("integer", Required: true))]), [new("query", "page", L(3))], [], [], [], []);
        MappingRequestPreparation.Preview(new(config, []), Catalog(), Owner).Errors.Should().BeEmpty();
        MappingEvaluator.Evaluate(new("integer", Args: [L("5")]), []).GetDecimal().Should().Be(5);
        Action invalid = () => MappingEvaluator.Evaluate(new("integer", Args: [L(2.1m)]), []); invalid.Should().Throw<FormatException>();
    }
    [Fact]
    public void OutputDependenciesAlsoParticipateInCycleDetection()
    {
        var config = new DesignerMappingConfiguration(1, new("GET", "/", []), [], [], [new("out", "Rows", Catalog().Sources[2].Schema, S(Items))], [], []);
        var catalog = Catalog() with { Dependencies = [.. Catalog().Dependencies!, new(new(Definition, Version, "event"), Owner, "Source")] };
        MappingValidation.Validate(config, catalog, Owner).Should().Contain(e => e.Contains("cycle"));
    }
}
