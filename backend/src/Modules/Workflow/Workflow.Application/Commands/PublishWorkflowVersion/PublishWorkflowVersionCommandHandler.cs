namespace Workflow.Application.Commands.PublishWorkflowVersion;

using System.Text.Json;
using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Repositories;

public sealed class PublishWorkflowVersionCommandHandler
    : IRequestHandler<PublishWorkflowVersionCommand, Result<WorkflowVersionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IWorkflowXmlCompiler _compiler;

    public PublishWorkflowVersionCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowVersionRepository versionRepo,
        IWorkflowXmlCompiler compiler)
    {
        _gate = gate;
        _definitionRepo = definitionRepo;
        _versionRepo = versionRepo;
        _compiler = compiler;
    }

    public async Task<Result<WorkflowVersionDto>> Handle(
        PublishWorkflowVersionCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDto>(gateResult.Error);

        var version = await _versionRepo.GetByIdWithProjectionAsync(
            request.VersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotFound);

        if (!version.IsDraft)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.NotDraft);

        var definition = await _definitionRepo.GetByIdAsync(
            version.WorkflowDefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.NotFound);

        if (!definition.IsActive)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.InactiveCannotPublish);

        if (string.IsNullOrWhiteSpace(version.XmlContent))
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.InvalidXml);

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
        version.SetValidationResult(isValid, JsonSerializer.Serialize(resultDto), DateTime.UtcNow);

        if (!isValid)
        {
            await _versionRepo.SaveChangesAsync(cancellationToken);
            var first = errors[0].Message;
            return Result.Failure<WorkflowVersionDto>(new NWFM.Shared.Results.Error(
                WorkflowErrors.Version.ValidationFailed.Code,
                first));
        }

        var now = DateTime.UtcNow;
        version.Publish(request.PublishedByUserId, now);
        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(CreateWorkflowDraftCommandHandler.MapToDto(version));
    }
}
