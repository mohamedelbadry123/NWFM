using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Workflow.Api.Controllers;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Models;

namespace NWFM.Tests.Modules.Workflow;

public sealed class DesignerMappingApiTests
{
    [Fact]
    public void InferenceApiRejectsUnusableSuggestionsAndLabelsValidSuggestions()
    {
        var controller = new WorkflowMappingController(Mock.Of<IDesignerMappingCatalog>());
        foreach (var sample in new[] { "{}", "[]", "null", "{\"nested\":{}}", "{\"\":1}" })
            controller.Infer(JsonSerializer.Deserialize<JsonElement>(sample)).Should().BeOfType<BadRequestObjectResult>();
        controller.Infer(JsonSerializer.SerializeToElement(Enumerable.Range(0, 500).ToDictionary(i => "x" + i, i => i))).Should().BeOfType<BadRequestObjectResult>();
        var result = (OkObjectResult)controller.Infer(JsonSerializer.Deserialize<JsonElement>("{\"value\":2}"));
        ((MappingSchema)result.Value!).Inferred.Should().BeTrue();
        ((MappingSchema)result.Value!).Properties!["value"].Required.Should().BeFalse();
    }
    [Fact]
    public async Task PurePreviewReturnsPreparedJsonAndValidationAllowsRuntimePublication()
    {
        var owner = new CallableTarget(Guid.NewGuid(), Guid.NewGuid(), "event");
        var catalog = new Mock<IDesignerMappingCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.GetAsync(owner.WorkflowDefinitionId, owner.WorkflowVersionId, default)).ReturnsAsync(Result.Success(new MappingCatalog([], [], [owner])));
        var controller = new WorkflowMappingController(catalog.Object);
        var config = new DesignerMappingConfiguration(1, new("GET", "/v1/items", []), [], [], [], [], []);
        var preview = (OkObjectResult)await controller.Preview(owner.WorkflowDefinitionId, owner.WorkflowVersionId, new(config, [], owner), default);
        var result = (MappingPreviewResult)preview.Value!; result.Errors.Should().BeEmpty(); result.Request!.Path.Should().Be("/v1/items");
        JsonSerializer.Serialize(result, IntegrationJson.Options).Should().NotContain("body");
        var validation = (OkObjectResult)await controller.Validate(owner.WorkflowDefinitionId, owner.WorkflowVersionId, new(owner, config), default);
        JsonSerializer.SerializeToElement(validation.Value).GetProperty("canPublish").GetBoolean().Should().BeTrue();
        typeof(WorkflowMappingController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy.Should().Be(NwfmPolicies.ManageDefinitions);
        catalog.VerifyAll(); catalog.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task UnknownRelatedVersionIsRejectedBeforeAnyValueLookup()
    {
        var owner = new CallableTarget(Guid.NewGuid(), Guid.NewGuid(), "event"); var catalog = new Mock<IDesignerMappingCatalog>(MockBehavior.Strict);
        catalog.Setup(c => c.GetAsync(owner.WorkflowDefinitionId, owner.WorkflowVersionId, default)).ReturnsAsync(Result.Success(new MappingCatalog([], [], [owner])));
        catalog.Setup(c => c.RelatedAsync(default)).ReturnsAsync([]);
        var config = new DesignerMappingConfiguration(1, new("GET", "/", []), [], [new("related", Guid.NewGuid(), Guid.NewGuid(), "BusinessKey", new("literal", JsonSerializer.SerializeToElement("key")))], [], [], []);
        (await new WorkflowMappingController(catalog.Object).Preview(owner.WorkflowDefinitionId, owner.WorkflowVersionId, new(config, [], owner), default)).Should().BeOfType<BadRequestObjectResult>();
    }
    [Fact]
    public async Task TypedSendTestRoutesOnlyThroughPreparedTransportContract()
    {
        var integrations = new Mock<IWorkflowIntegrations>(MockBehavior.Strict);
        integrations.Setup(i => i.TestPreparedHttpAsync(It.IsAny<HttpActivityConfiguration>(), It.IsAny<PreparedMappingRequest>(), default)).ReturnsAsync(Result.Failure<IntegrationResult>(new Error("Mapping.PreparedInvalid", "Typed endpoint required.")));
        var controller = new WorkflowIntegrationsController(integrations.Object);
        (await controller.Test(new(new HttpActivityConfiguration(), [], new("GET", "/", [], [])), default)).Should().BeOfType<BadRequestObjectResult>();
        integrations.Verify(i => i.TestPreparedHttpAsync(It.IsAny<HttpActivityConfiguration>(), It.IsAny<PreparedMappingRequest>(), default), Times.Once);
        integrations.VerifyNoOtherCalls();
    }
    [Fact]
    public void MalformedImportReturnsClientError()
    {
        var controller = new WorkflowMappingController(Mock.Of<IDesignerMappingCatalog>());
        controller.Import(new("{\"openapi\":\"3.0.3\"}", "/missing", "GET")).Should().BeOfType<BadRequestObjectResult>();
    }
    [Theory]
    [InlineData("UserTask", "{\"typedMapping\":null}")]
    [InlineData("ServiceTask", "{\"typedMapping\":{}}")]
    [InlineData("ServiceTask", "{\"mappingMode\":\"Typed\"}")]
    public void PublishGateRejectsMalformedOrImplicitTypedConfigurations(string type, string json)
    {
        var errors = new List<WorkflowValidationIssueDto>();
        WorkflowActivityConfigurationValidator.Validate(new WorkflowXmlDocument { Activities = [new() { NodeKey = "event", ActivityTypeName = type, ConfigurationJson = json }] }, errors, []);
        errors.Should().Contain(e => e.Code == "MAPPING_CONFIG_INVALID");
    }
}
