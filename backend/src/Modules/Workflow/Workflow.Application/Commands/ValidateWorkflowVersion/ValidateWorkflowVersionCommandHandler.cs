namespace Workflow.Application.Commands.ValidateWorkflowVersion;

using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Application.Integrations;
using Workflow.Domain.Repositories;

public sealed class ValidateWorkflowVersionCommandHandler
    : IRequestHandler<ValidateWorkflowVersionCommand, Result<WorkflowValidationResultDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowXmlCompiler _compiler;
    private readonly IWorkflowActionRegistry? _actionRegistry;
    private readonly IWorkflowIntegrations? _integrations;
    private readonly Workflow.Application.Workspace.IWorkflowWorkspacePublisher? _workspace;
    private readonly IWorkflowDraftLock? _draftLock;

    public ValidateWorkflowVersionCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowVersionRepository versionRepo,
        IWorkflowXmlCompiler compiler,
        IWorkflowActionRegistry? actionRegistry = null, IWorkflowIntegrations? integrations = null, Workflow.Application.Workspace.IWorkflowWorkspacePublisher? workspace = null,
        IWorkflowDraftLock? draftLock = null)
    {
        _gate = gate;
        _versionRepo = versionRepo;
        _compiler = compiler;
        _actionRegistry = actionRegistry;
        _integrations = integrations;
        _workspace = workspace;
        _draftLock = draftLock;
    }

    public async Task<Result<WorkflowValidationResultDto>> Handle(
        ValidateWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            return _draftLock is null ? await ValidateAsync(request, cancellationToken)
                : await _draftLock.RunAsync(request.VersionId, () => ValidateAsync(request, cancellationToken), cancellationToken);
        }
        catch (DbUpdateConcurrencyException conflict)
        {
            // A writer outside the draft lock (for example publication) still wins by rowversion.
            // Discard our stale validation before returning a retryable conflict to the caller.
            foreach (var entry in conflict.Entries) await entry.ReloadAsync(cancellationToken);
            return Result.Failure<WorkflowValidationResultDto>(new Error("Workflow.Draft.Conflict", "The draft changed during validation. Validate the latest revision again."));
        }
    }

    private async Task<Result<WorkflowValidationResultDto>> ValidateAsync(
        ValidateWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowValidationResultDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdWithProjectionAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowValidationResultDto>(WorkflowErrors.Version.NotFound);

        if (!version.IsDraft)
            return Result.Failure<WorkflowValidationResultDto>(WorkflowErrors.Version.NotDraft);

        var resultDto = await CheckAsync(version, _compiler, _actionRegistry, _integrations, _workspace, cancellationToken);
        var isValid = resultDto.IsValid;
        var resultJson = JsonSerializer.Serialize(resultDto);

        version.SetValidationResult(isValid, resultJson, DateTime.UtcNow);
        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(resultDto);
    }
    public static async Task<WorkflowValidationResultDto> CheckAsync(
        Workflow.Domain.Entities.WorkflowVersion version, IWorkflowXmlCompiler compiler,
        IWorkflowActionRegistry? actionRegistry, IWorkflowIntegrations? integrations,
        Workflow.Application.Workspace.IWorkflowWorkspacePublisher? workspace, CancellationToken cancellationToken)
    {
        var compileResult = compiler.Compile(version.XmlContent, out _);
        var errors = new List<WorkflowValidationIssueDto>();
        var warnings = new List<WorkflowValidationIssueDto>();

        if (compileResult.IsFailure)
        {
            errors.Add(new WorkflowValidationIssueDto("XML_INVALID", compileResult.Error.Message));
        }
        else
        {
            WorkflowGraphValidator.Validate(compileResult.Value!, errors, warnings, actionRegistry);
            if (integrations is not null) errors.AddRange(await integrations.ValidateConnectionsAsync(compileResult.Value!, cancellationToken));
        }

        if (workspace is not null) errors.AddRange(await workspace.ValidateAsync(version, cancellationToken));
        var isValid = errors.Count == 0;
        var resultDto = new WorkflowValidationResultDto(isValid, errors.Distinct().ToArray(), warnings.Select(w => w with { Severity = "Warning" }).Distinct().ToArray());
        return resultDto;
    }

}
