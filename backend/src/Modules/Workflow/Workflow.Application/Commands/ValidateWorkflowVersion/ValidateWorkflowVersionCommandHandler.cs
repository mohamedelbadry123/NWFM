namespace Workflow.Application.Commands.ValidateWorkflowVersion;

using System.Text.Json;
using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Repositories;

public sealed class ValidateWorkflowVersionCommandHandler
    : IRequestHandler<ValidateWorkflowVersionCommand, Result<WorkflowValidationResultDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowXmlCompiler _compiler;

    public ValidateWorkflowVersionCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowVersionRepository versionRepo,
        IWorkflowXmlCompiler compiler)
    {
        _gate = gate;
        _versionRepo = versionRepo;
        _compiler = compiler;
    }

    public async Task<Result<WorkflowValidationResultDto>> Handle(
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

        var compileResult = _compiler.Compile(version.XmlContent, out _);
        var errors = new List<WorkflowValidationIssueDto>();
        var warnings = new List<WorkflowValidationIssueDto>();

        if (compileResult.IsFailure)
        {
            errors.Add(new WorkflowValidationIssueDto("XML_INVALID", compileResult.Error.Message));
        }
        else
        {
            WorkflowGraphValidator.Validate(compileResult.Value!, errors, warnings);
        }

        var isValid = errors.Count == 0;
        var resultDto = new WorkflowValidationResultDto(isValid, errors, warnings);
        var resultJson = JsonSerializer.Serialize(resultDto);

        version.SetValidationResult(isValid, resultJson, DateTime.UtcNow);
        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(resultDto);
    }
}
