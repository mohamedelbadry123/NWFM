using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Routing;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Modules.Workflow;

public sealed class WorkflowRoutingSaveTests
{
    [Fact]
    public async Task DraftRevision_AllowsCurrentSave_AndRejectsStalePresentationAndXmlWrites()
    {
        const string xml = "<Workflow xmlns=\"https://privora.io/workflow/v1\" workspaceJson=\"{&quot;designerVersion&quot;:4}\"><Activities><Activity nodeKey=\"start\" type=\"Start\" name=\"Start\"/><Activity nodeKey=\"end\" type=\"End\" name=\"End\"/></Activities><Transitions><Transition key=\"finish\" from=\"start\" to=\"end\"/></Transitions></Workflow>";
        var now = DateTime.UtcNow;
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), now);
        version.SetWorkspace("{\"designerVersion\":4}");
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var repository = new Mock<IWorkflowVersionRepository>();
        repository.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var handler = new SaveWorkflowDraftXmlCommandHandler(gate.Object, repository.Object, new WorkflowXmlCompiler(), Options.Create(new WorkflowSettings { RoutingV4Enabled = true }));
        var first = await handler.Handle(new(version.Id, xml, "{}", ""), default);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : "");
        var revision = first.Value.Revision;
        var second = await handler.Handle(new(version.Id, xml, "{\"zoom\":0.8}", revision), default);
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Message : "");
        second.Value.Revision.Should().NotBe(revision);
        var stale = await handler.Handle(new(version.Id, xml, "{\"zoom\":2}", revision), default);
        stale.IsFailure.Should().BeTrue(); stale.Error.Code.Should().Be("Workflow.Draft.Conflict");
        version.DesignerJson.Should().Be("{\"zoom\":0.8}");
        RoutingDraftRevision.Of(version).Should().Be(second.Value.Revision);
        var configuredXml = xml.Replace("name=\"End\"", "name=\"End\" configurationJson=\"{&quot;note&quot;:&quot;changed&quot;}\"");
        var configured = await handler.Handle(new(version.Id, configuredXml, version.DesignerJson, second.Value.Revision), default);
        configured.IsSuccess.Should().BeTrue();
        configured.Value.Revision.Should().NotBe(second.Value.Revision, "configuration changes must participate even when the legacy structural XML hash stays unchanged");
        (await handler.Handle(new(version.Id, xml, version.DesignerJson, second.Value.Revision), default)).Error.Code.Should().Be("Workflow.Draft.Conflict");
    }
}
