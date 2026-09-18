namespace Workflow.Application.Queries.ListWorkflowDefinitions;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowDefinition;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowDefinitionsQueryHandler
    : IRequestHandler<ListWorkflowDefinitionsQuery, Result<PaginatedResult<WorkflowDefinitionDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDefinitionRepository _repo;

    public ListWorkflowDefinitionsQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowDefinitionRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowDefinitionDto>>> Handle(
        ListWorkflowDefinitionsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowDefinitionDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.PageNumber, request.PageSize,
            request.SearchTerm, request.OrganizationId, cancellationToken);

        var dtos = new List<WorkflowDefinitionDto>(items.Count);
        foreach (var d in items)
        {
            var count = await _repo.GetVersionCountAsync(d.Id, cancellationToken);
            dtos.Add(CreateWorkflowDefinitionCommandHandler.MapToDto(d, count));
        }

        return Result.Success(new PaginatedResult<WorkflowDefinitionDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
