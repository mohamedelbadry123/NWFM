namespace Workflow.Application.Queries.ListWorkflowVersions;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowVersionsQueryHandler
    : IRequestHandler<ListWorkflowVersionsQuery, Result<PaginatedResult<WorkflowVersionDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _definitionRepo;
    private readonly IWorkflowVersionRepository _versionRepo;

    public ListWorkflowVersionsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDefinitionRepository definitionRepo,
        IWorkflowVersionRepository versionRepo)
    {
        _gate = gate;
        _definitionRepo = definitionRepo;
        _versionRepo = versionRepo;
    }

    public async Task<Result<PaginatedResult<WorkflowVersionDto>>> Handle(
        ListWorkflowVersionsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowVersionDto>>(gateResult.Error);

        var definitionExists = await _definitionRepo.GetByIdAsync(
            request.DefinitionId, cancellationToken);
        if (definitionExists is null)
            return Result.Failure<PaginatedResult<WorkflowVersionDto>>(WorkflowErrors.Definition.NotFound);

        var (items, total) = await _versionRepo.GetPagedByDefinitionAsync(
            request.DefinitionId,
            request.PageNumber, request.PageSize, cancellationToken);

        var dtos = items.Select(CreateWorkflowDraftCommandHandler.MapToDto).ToList();

        return Result.Success(new PaginatedResult<WorkflowVersionDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
