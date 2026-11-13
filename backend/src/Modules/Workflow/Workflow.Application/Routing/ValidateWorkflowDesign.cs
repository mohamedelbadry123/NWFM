using MediatR;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Commands.ValidateWorkflowVersion;
using Workflow.Application.DTOs;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Repositories;

namespace Workflow.Application.Routing;

public sealed record ValidateWorkflowDesign(Guid DefinitionId, Guid VersionId, string XmlContent, string? DesignerJson, long EditId) : IRequest<Result<WorkflowDesignValidation>>;
public sealed record WorkflowDesignValidation(long EditId, string Revision, WorkflowValidationResultDto Result);
public sealed class ValidateWorkflowDesignHandler(IWorkflowVersionRepository versions, IWorkflowXmlCompiler compiler,
    IWorkflowFeatureGate gate, IWorkflowWorkspacePublisher workspace,
    IWorkflowActionRegistry? actions = null, IWorkflowIntegrations? integrations = null)
    : IRequestHandler<ValidateWorkflowDesign, Result<WorkflowDesignValidation>>
{
    public async Task<Result<WorkflowDesignValidation>> Handle(ValidateWorkflowDesign input, CancellationToken ct)
    {
        var allowed = gate.EnsureEnabled();
        if (allowed.IsFailure) return Result.Failure<WorkflowDesignValidation>(allowed.Error);
        var version = await versions.GetByIdWithProjectionAsync(input.VersionId, ct);
        if (version is null || version.WorkflowDefinitionId != input.DefinitionId)
            return Result.Failure<WorkflowDesignValidation>(new("Workflow.Version.NotFound", "Workflow version not found."));
        if (!version.IsDraft) return Result.Failure<WorkflowDesignValidation>(new("Workflow.Version.NotDraft", "Only drafts can be checked."));
        if (input.XmlContent is null || input.XmlContent.Length > 512 * 1024)
            return Result.Failure<WorkflowDesignValidation>(new("Workflow.InvalidDraft", "The draft is too large."));
        var xml = input.XmlContent;
        try { xml = WorkspaceDesign.NormalizeDraft(xml); }
        catch (Exception e) when (e is System.Xml.XmlException or System.Text.Json.JsonException or InvalidOperationException)
        { return Result.Success(new WorkflowDesignValidation(input.EditId, RoutingDraftRevision.Of(version), new(false, [new("XML_INVALID", "Review the workflow definition.")], []))); }
        var compiled = compiler.Compile(xml, out _);
        WorkflowValidationResultDto result;
        if (compiled.IsFailure) result = new(false, [new("XML_INVALID", compiled.Error.Message)], []);
        else
        {
            var document = compiled.Value!;
            var projection = SaveWorkflowDraftXmlCommandHandler.BuildProjection(version.Id, document, DateTime.UtcNow);
            var snapshot = version.CopyForValidation(xml, input.DesignerJson, document.WorkspaceJson, projection.Activities, projection.Transitions, projection.Variables, projection.Rules, projection.Outcomes, projection.Actions);
            result = await ValidateWorkflowVersionCommandHandler.CheckAsync(snapshot, compiler, actions, integrations, workspace, ct);
        }
        return Result.Success(new WorkflowDesignValidation(input.EditId, RoutingDraftRevision.Of(version), result));
    }
}
