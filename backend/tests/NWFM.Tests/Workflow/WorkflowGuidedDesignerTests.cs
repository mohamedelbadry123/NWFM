using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.DTOs;
using Workflow.Application.Routing;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Modules.Workflow;

public sealed class WorkflowGuidedDesignerTests
{
    private static WorkflowVersion Draft(string configuration = "{}", string? expression = null)
    {
        XNamespace ns = "https://privora.io/workflow/v1";
        var xml = new XElement(ns + "Workflow", new XAttribute("workspaceJson", "{\"designerVersion\":3}"),
            new XElement(ns + "Activities", new XElement(ns + "Activity", new XAttribute("nodeKey", "start"), new XAttribute("type", "Start"), new XAttribute("name", "Start")),
                new XElement(ns + "Activity", new XAttribute("nodeKey", "registration"), new XAttribute("type", "UserTask"), new XAttribute("name", "Registration"), new XAttribute("configurationJson", configuration)),
                new XElement(ns + "Activity", new XAttribute("nodeKey", "end"), new XAttribute("type", "End"), new XAttribute("name", "End"))),
            new XElement(ns + "Transitions", new XElement(ns + "Transition", new XAttribute("key", "entry"), new XAttribute("from", "start"), new XAttribute("to", "registration")),
                new XElement(ns + "Transition", new XAttribute("key", "finish"), new XAttribute("from", "registration"), new XAttribute("to", "end"), new XAttribute("condition", expression ?? "")))).ToString();
        var compiler = new WorkflowXmlCompiler(); var compiled = compiler.Compile(xml, out _); compiled.IsSuccess.Should().BeTrue();
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        var projection = SaveWorkflowDraftXmlCommandHandler.BuildProjection(version.Id, compiled.Value!, DateTime.UtcNow);
        return version.CopyForValidation(xml, "{}", compiled.Value!.WorkspaceJson, projection.Activities, projection.Transitions, projection.Variables);
    }
    [Fact]
    public void CompactAlwaysRoutes_ValidateAndSimulateWithoutRedundantConditionNodes()
    {
        var graph = new RoutingGraph([new("start", "Start", "Start"), new("registration", "Registration", "UserTask"), new("end", "End", "End")],
            [new("entry", "start", "registration"), new("finish", "registration", "end")]);
        RoutingGraphRules.Validate(graph).Should().BeEmpty();
        var simulation = RoutingSimulator.Run(graph, new([new("registration")]));
        simulation.Diagnostics.Should().BeEmpty(); simulation.Waiting.Should().BeEmpty();
    }
    [Fact]
    public void Preparation_PreservesCanvasIdentityAndPresentationWithoutRestoringLegacyRules()
    {
        var version = Draft(expression: "custom.Value > 42");
        version.UpdateDesignerJson("{\"zoom\":1.3,\"nodes\":[{\"id\":\"canvas-registration\",\"nodeKey\":\"registration\",\"x\":333,\"y\":444,\"configurationJson\":\"{\\\"legacy\\\":true}\"}],\"edges\":[]}", DateTime.UtcNow);
        var converted = RoutingUpgrade.Convert(version, [], preserveRepairs: true);
        var presentation = JsonDocument.Parse(RoutingUpgrade.PrepareDesignerJson(version, converted.Xml));
        presentation.RootElement.GetProperty("zoom").GetDouble().Should().Be(1.3);
        var nodes = presentation.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var registration = nodes.Single(n => n.GetProperty("nodeKey").GetString() == "registration");
        registration.GetProperty("id").GetString().Should().Be("canvas-registration");
        registration.GetProperty("x").GetDouble().Should().Be(333);
        registration.GetProperty("configurationJson").GetString().Should().NotContain("legacy");
        nodes.Should().Contain(n => n.GetProperty("type").GetString() == "ExclusiveGateway");
    }
    [Fact]
    public void Authoring_ProvesOverlappingNumericRules_WithoutChangingPublishedEvaluation()
    {
        var config = WorkflowRoutingTests.Rule("step", "amount", "numeric", "greater_than", 10, "one");
        config.Routes.Add(WorkflowRoutingTests.Rule("step", "amount", "numeric", "greater_than", 20, "two").Routes[0]);
        RoutingRuleConflicts.Find(config).Should().Contain("two");
        RoutingEvaluator.Evaluate(config, new Dictionary<string, object?> { ["step.$outcome"] = "success", ["step.amount"] = 15 }).Transitions.Should().Equal("one");
        config.MatchMode = "First"; RoutingRuleConflicts.Find(config).Should().BeEmpty();
    }
    [Fact]
    public void Preparation_HandlesFreshDraftInheritedFromPublishedVersionThree()
    {
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 2, Guid.NewGuid(), DateTime.UtcNow);
        version.SetWorkspace("{\"designerVersion\":3}");
        var result = RoutingUpgrade.Convert(version, [], preserveRepairs: true);
        result.Xml.Should().Contain("Start"); result.Xml.Should().Contain("End");
        result.Diagnostics.Should().BeEmpty(); version.XmlContent.Should().BeEmpty();
    }
    [Fact]
    public void Preparation_KeepsSimpleAlwaysRouteWithoutExtraConditions()
    {
        var result = RoutingUpgrade.Convert(Draft(), [], preserveRepairs: true);
        result.ConditionsAdded.Should().Be(0); result.Xml.Should().Contain("designerVersion&quot;:4");
        result.Xml.Should().Contain("registration");
    }
    [Fact]
    public void Preparation_RetainsUnsupportedExpressionForExplicitRepair()
    {
        var version = Draft(expression: "custom.Value > 42"); var original = version.XmlContent;
        var result = RoutingUpgrade.Convert(version, [], preserveRepairs: true);
        result.Xml.Should().Contain("originalJson"); result.Xml.Should().Contain("custom.Value");
        result.Xml.Should().Contain("authoring"); version.XmlContent.Should().Be(original);
        var doc = XDocument.Parse(result.Xml);
        doc.Descendants().Where(e => e.Name.LocalName == "Transition").Should().OnlyContain(e => e.Attribute("condition") == null || e.Attribute("condition")!.Value == "");
    }
    [Fact]
    public void Preparation_RetainsMissingDestinationAndOriginalPredicate()
    {
        var config = JsonSerializer.Serialize(new { formSteps = new[] { new { id = "form", formId = Guid.NewGuid(), versionNo = 1, name = "Registration", otherwise = new { kind = "Next" }, rules = new[] { new { id = "r", name = "Has customer", when = new { match = "All", conditions = new[] { new { field = "customer", @operator = "is_not_empty" } }, groups = Array.Empty<object>() }, destination = new { kind = "Next" } } } } } });
        var converted = RoutingUpgrade.Convert(Draft(config), [], preserveRepairs: true);
        converted.Xml.Should().Contain("Choose where Registration continues"); converted.Xml.Should().Contain("registration.customer");
        converted.Xml.Should().Contain("originalJson");
    }
    [Fact]
    public async Task LiveCheck_ValidatesSnapshotWithoutSavingOrChangingStoredDraft()
    {
        var version = Draft(); var xml = version.XmlContent;
        var repository = new Mock<IWorkflowVersionRepository>();
        repository.Setup(r => r.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var workspace = new Mock<IWorkflowWorkspacePublisher>(); workspace.Setup(w => w.ValidateAsync(It.IsAny<WorkflowVersion>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new WorkflowValidationIssueDto("MISSING", "Choose a form.", "registration", "form") });
        var input = XDocument.Parse(xml);
        var registration = input.Descendants().Single(e => e.Name.LocalName == "Activity" && e.Attribute("nodeKey")?.Value == "registration");
        registration.Add(new XElement(registration.Name.Namespace + "AssignmentRule", new XAttribute("assigneeType", "AssignmentGroup"), new XAttribute("referenceId", Guid.NewGuid())));
        var handler = new ValidateWorkflowDesignHandler(repository.Object, new WorkflowXmlCompiler(), gate.Object, workspace.Object);
        var result = await handler.Handle(new(version.WorkflowDefinitionId, version.Id, input.ToString(), "{}", 17), default);
        result.IsSuccess.Should().BeTrue(); result.Value.EditId.Should().Be(17); result.Value.Result.Errors.Should().Contain(e => e.Code == "MISSING");
        workspace.Verify(w => w.ValidateAsync(It.Is<WorkflowVersion>(v => v.Activities.Any(a => a.NodeKey == "registration" && a.AssignmentRules.Count == 1)), It.IsAny<CancellationToken>()), Times.Once);
        version.Activities.Single(a => a.NodeKey == "registration").AssignmentRules.Should().BeEmpty();
        version.XmlContent.Should().Be(xml); version.ValidationResultJson.Should().BeNull();
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        (await handler.Handle(new(Guid.NewGuid(), version.Id, xml, "{}", 18), default)).IsFailure.Should().BeTrue();
    }
}
