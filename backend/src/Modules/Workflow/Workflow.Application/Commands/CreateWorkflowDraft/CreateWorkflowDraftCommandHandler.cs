namespace Workflow.Application.Commands.CreateWorkflowDraft;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateWorkflowDraftCommandHandler
    : IRequestHandler<CreateWorkflowDraftCommand, Result<WorkflowVersionDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowVersionRepository _versionRepo;

    public CreateWorkflowDraftCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _definitionRepo = definitionRepo;
        _versionRepo = versionRepo;
    }

    public async Task<Result<WorkflowVersionDto>> Handle(
        CreateWorkflowDraftCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowVersionDto>(gateResult.Error);

        var definition = await _definitionRepo.GetByIdAsync(
            request.DefinitionId, cancellationToken);
        if (definition is null)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.NotFound);

        if (!definition.IsActive)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Definition.InactiveCannotPublish);

        var hasDraft = await _versionRepo.HasDraftAsync(
            request.DefinitionId, cancellationToken);
        if (hasDraft)
            return Result.Failure<WorkflowVersionDto>(WorkflowErrors.Version.DraftAlreadyExists);

        var nextNumber = await _versionRepo.GetNextVersionNumberAsync(
            request.DefinitionId, cancellationToken);

        var now = DateTime.UtcNow;
        var version = WorkflowVersion.CreateDraft(
            request.DefinitionId,
            nextNumber,
            request.CreatedByUserId,
            now,
            request.ChangeSummary);

        await _versionRepo.AddAsync(version, cancellationToken);
        await _versionRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(version));
    }

    internal static WorkflowVersionDto MapToDto(WorkflowVersion v) =>
        new(v.Id, v.WorkflowDefinitionId, v.VersionNumber,
            v.Status, v.SchemaVersion, v.ValidationStatus,
            v.ChangeSummary, v.CreatedByUserId, v.PublishedByUserId,
            v.PublishedAt, v.CreatedAt, v.UpdatedAt);
}
