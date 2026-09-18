namespace Workflow.Application.Queries.ListAllWorkflowBindings;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.CreateWorkflowBinding;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListAllWorkflowBindingsQueryHandler
    : IRequestHandler<ListAllWorkflowBindingsQuery, Result<PaginatedResult<WorkflowBindingDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _repo;

    public ListAllWorkflowBindingsQueryHandler(
        IWorkflowFeatureGate gate, IWorkflowBindingRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowBindingDto>>> Handle(
        ListAllWorkflowBindingsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowBindingDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAllAsync(
            request.PageNumber, request.PageSize,
            request.DefinitionId, request.OrganizationId,
            cancellationToken);

        var dtos = items.Select(b => CreateWorkflowBindingCommandHandler.MapToDto(b)).ToList();

        return Result.Success(new PaginatedResult<WorkflowBindingDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
