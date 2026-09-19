namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using NWFM.Shared.Integration.Workflow;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Helpers;
using global::Workflow.Application.Models;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ValidateWorkflowVersion;
using global::Workflow.Application.Commands.PublishWorkflowVersion;
using global::Workflow.Application.Queries.GetWorkflowPublishPreview;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;
using NWFM.Shared.Results;

public sealed class WorkflowActivityConfigurationValidatorTests
{
    [Theory]
    [InlineData("WaitEvent", "{}", "EVENT_KEY_REQUIRED")]
    [InlineData("WaitEvent", "{\"eventKey\":\"ready\",\"correlationVariable\":\"orderId\"}", "EVENT_CORRELATION_UNSUPPORTED")]
    [InlineData("Timer", "{\"timerType\":\"ExternalSignal\",\"signalKey\":\"ready\"}", "TIMER_SIGNAL_UNSUPPORTED")]
    [InlineData("Timer", "{\"duration\":\"-00:10:00\"}", "TIMER_DURATION_INVALID")]
    [InlineData("Timer", "{\"timerType\":\"DueDate\",\"dueAt\":\"2026-09-19T15:00\"}", "TIMER_DATE_INVALID")]
    [InlineData("CallActivity", "{\"definitionKey\":\"\"}", "CALL_ACTIVITY_CONFIG")]
    [InlineData("CallActivity", "{\"definitionKey\":\"child\",\"waitForCompletion\":\"true\"}", "CALL_ACTIVITY_WAIT_INVALID")]
    [InlineData("ScriptTask", "{\"setVariables\":\"invalid\"}", "VARIABLE_ASSIGNMENTS_INVALID")]
    [InlineData("ScriptTask", "{\"setVariables\":[42]}", "VARIABLE_ASSIGNMENTS_INVALID")]
    [InlineData("NotificationTask", "{\"templateKey\":\"notice\",\"failurePolicy\":\"Retry\"}", "NOTIFICATION_RETRY_UNSUPPORTED")]
    [InlineData("ServiceTask", "{}", "SERVICE_ACTION_REQUIRED")]
    [InlineData("WaitEvent", "[]", "ACTIVITY_CONFIG_OBJECT")]
    [InlineData("WaitEvent", "{", "ACTIVITY_CONFIG_JSON")]
    public void InvalidOrUnsupportedConfiguration_IsReportedAgainstItsNode(string type, string configuration, string code)
    {
        var errors = Validate(type, configuration);
        errors.Should().Contain(e => e.Code == code && e.NodeKey == "activity");
    }

    [Theory]
    [InlineData("WaitEvent", "{\"eventKey\":\"ready\"}")]
    [InlineData("WaitEvent", "{\"signalKey\":\"ready\"}")]
    [InlineData("Timer", "{\"duration\":\"00:00:00\"}")]
    [InlineData("Timer", "{\"timerType\":\"DueDate\",\"dueAt\":\"2026-09-19T15:00:00+03:00\"}")]
    [InlineData("CallActivity", "{\"definitionKey\":\"child\",\"waitForCompletion\":true}")]
    [InlineData("ScriptTask", "{\"setVariables\":{\"amount\":42}}")]
    [InlineData("ScriptTask", "{\"setVariables\":[{\"name\":\"amount\",\"value\":42}]}")]
    [InlineData("NotificationTask", "{\"templateKey\":\"notice\",\"failurePolicy\":\"FailWorkflow\"}")]
    public void SupportedConfiguration_Passes(string type, string configuration)
        => Validate(type, configuration).Should().BeEmpty();

    [Fact]
    public void MissingServiceProvider_IsRejectedBeforePublication()
    {
        var registry = new Mock<IWorkflowActionRegistry>();
        var errors = new List<WorkflowValidationIssueDto>();
        WorkflowActivityConfigurationValidator.Validate(new WorkflowXmlDocument
        {
            Activities = [new ActivityXmlNode { NodeKey = "api", ActivityTypeName = "ServiceTask", ActionKey = "missing" }]
        }, errors, [], registry.Object);
        errors.Should().ContainSingle(e => e.Code == "SERVICE_ACTION_UNAVAILABLE");
    }

    [Fact]
    public async Task ValidatePreviewAndPublish_AllRejectAnUnavailableAction()
    {
        var now = DateTime.UtcNow;
        var actor = Guid.NewGuid();
        var definition = WorkflowDefinition.Create(Guid.NewGuid(), "api", "API", now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, actor, now);
        version.UpdateXml("""
            <Workflow xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" />
                <Activity nodeKey="api" type="ServiceTask" name="API" actionKey="missing" />
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="enter" from="start" to="api" />
                <Transition key="exit" from="api" to="end" />
              </Transitions>
            </Workflow>
            """, "hash", now);
        var gate = new Mock<IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var versions = new Mock<IWorkflowVersionRepository>();
        versions.Setup(v => v.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        versions.Setup(v => v.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var definitions = new Mock<IWorkflowDefinitionRepository>();
        definitions.Setup(d => d.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        var registry = new Mock<IWorkflowActionRegistry>();
        var compiler = new WorkflowXmlCompiler();

        var validation = await new ValidateWorkflowVersionCommandHandler(gate.Object, versions.Object, compiler, registry.Object)
            .Handle(new ValidateWorkflowVersionCommand(version.Id), default);
        validation.Value.IsValid.Should().BeFalse();
        validation.Value.Errors.Should().Contain(e => e.Code == "SERVICE_ACTION_UNAVAILABLE");

        // A stale Valid badge must not bypass current server capability checks.
        version.SetValidationResult(true, "{}", now);
        var preview = await new GetWorkflowPublishPreviewQueryHandler(gate.Object, definitions.Object, versions.Object, compiler, registry.Object)
            .Handle(new GetWorkflowPublishPreviewQuery(version.Id), default);
        preview.Value.CanPublish.Should().BeFalse();
        preview.Value.BlockingReasons.Should().Contain(message => message.Contains("not installed"));

        var publish = await new PublishWorkflowVersionCommandHandler(gate.Object, definitions.Object, versions.Object, compiler, registry.Object)
            .Handle(new PublishWorkflowVersionCommand(version.Id, actor), default);
        publish.IsFailure.Should().BeTrue();
        version.Status.Should().Be(WorkflowVersionStatus.Draft);
    }

    private static List<WorkflowValidationIssueDto> Validate(string type, string configuration)
    {
        var errors = new List<WorkflowValidationIssueDto>();
        WorkflowActivityConfigurationValidator.Validate(new WorkflowXmlDocument
        {
            Activities = [new ActivityXmlNode { NodeKey = "activity", ActivityTypeName = type, ConfigurationJson = configuration }]
        }, errors, []);
        return errors;
    }
}
