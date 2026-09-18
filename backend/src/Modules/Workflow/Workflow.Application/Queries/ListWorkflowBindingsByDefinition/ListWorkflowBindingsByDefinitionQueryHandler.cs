namespace Workflow.Application.Queries.ListWorkflowBindingsByDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowBindingsByDefinitionQueryHandler
    : IRequestHandler<ListWorkflowBindingsByDefinitionQuery, Result<PaginatedResult<WorkflowBindingDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public ListWorkflowBindingsByDefinitionQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowBindingDto>>> Handle(
        ListWorkflowBindingsByDefinitionQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowBindingDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedByDefinitionAsync(
            request.DefinitionId, request.PageNumber, request.PageSize, cancellationToken);

        var dtos = items.Select(b => CreateWorkflowBindingCommandHandler.MapToDto(b)).ToList();

        return Result.Success(new PaginatedResult<WorkflowBindingDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
