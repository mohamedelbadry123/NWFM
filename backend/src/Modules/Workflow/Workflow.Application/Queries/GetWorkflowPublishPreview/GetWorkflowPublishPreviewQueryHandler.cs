namespace Workflow.Application.Queries.GetWorkflowPublishPreview;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Application.Helpers;
using NWFM.Shared.Integration.Workflow;

public sealed class GetWorkflowPublishPreviewQueryHandler
    : IRequestHandler<GetWorkflowPublishPreviewQuery, Result<WorkflowPublishPreviewDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowXmlCompiler _compiler;
    private readonly IWorkflowActionRegistry? _actionRegistry;

    public GetWorkflowPublishPreviewQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitions,
        IWorkflowVersionRepository versions,
        IWorkflowXmlCompiler compiler,
        IWorkflowActionRegistry? actionRegistry = null)
    {
        _gate = gate;
        _definitions = definitions;
        _versions = versions;
        _compiler = compiler;
        _actionRegistry = actionRegistry;
    }

    public async Task<Result<WorkflowPublishPreviewDto>> Handle(
        GetWorkflowPublishPreviewQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowPublishPreviewDto>(gateResult.Error);

        var version = await _versions.GetByIdWithProjectionAsync(request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowPublishPreviewDto>(WorkflowErrors.Version.NotFound);

        var definition = await _definitions.GetByIdAsync(version.WorkflowDefinitionId, cancellationToken);
        var latestPublished = await _versions.GetLatestPublishedAsync(
            version.WorkflowDefinitionId, cancellationToken);

        var blocking = new List<string>();
        var warnings = new List<string>();

        if (!version.IsDraft)
            blocking.Add("Only Draft versions can be published.");
        if (definition is null)
            blocking.Add("Workflow definition was not found.");
        else if (!definition.IsActive)
            blocking.Add("Inactive definitions cannot publish new versions.");
        if (string.IsNullOrWhiteSpace(version.XmlContent))
            blocking.Add("Draft XML is empty.");
        if (version.ValidationStatus != WorkflowValidationStatus.Valid)
            blocking.Add("Validation status must be Valid before publish.");

        var requiredKeys = new List<string>();
        if (version.Activities.Count > 0)
        {
            requiredKeys = version.Activities
                .SelectMany(a => a.AssignmentRules)
                .Select(r => r.AssignmentKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }

        if (!string.IsNullOrWhiteSpace(version.XmlContent))
        {
            var compileResult = _compiler.Compile(version.XmlContent, out _);
            if (compileResult.IsFailure)
            {
                blocking.Add(compileResult.Error.Message);
            }
            else
            {
                var doc = compileResult.Value!;
                if (requiredKeys.Count == 0)
                {
                    requiredKeys = doc.Activities
                        .SelectMany(a => a.AssignmentRules)
                        .Select(r => r.AssignmentKey)
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()!;
                }

                var validationErrors = new List<WorkflowValidationIssueDto>();
                var validationWarnings = new List<WorkflowValidationIssueDto>();
                WorkflowGraphValidator.Validate(doc, validationErrors, validationWarnings, _actionRegistry);
                blocking.AddRange(validationErrors.Select(e => e.Message));
                warnings.AddRange(validationWarnings.Select(w => w.Message));
            }
        }

        if (latestPublished is not null && version.IsDraft)
            warnings.Add($"Publishing will supersede published version v{latestPublished.VersionNumber} for new starts.");

        return Result.Success(new WorkflowPublishPreviewDto(
            version.Id,
            version.WorkflowDefinitionId,
            version.VersionNumber,
            version.Status.ToString(),
            version.ValidationStatus.ToString(),
            CanPublish: blocking.Count == 0,
            BlockingReasons: blocking,
            Warnings: warnings,
            ActivityCount: version.Activities.Count,
            TransitionCount: version.Transitions.Count,
            VariableCount: version.Variables.Count,
            RequiredAssignmentKeys: requiredKeys,
            LatestPublishedVersionId: latestPublished?.Id,
            LatestPublishedVersionNumber: latestPublished?.VersionNumber,
            ChangeSummary: version.ChangeSummary));
    }
}
