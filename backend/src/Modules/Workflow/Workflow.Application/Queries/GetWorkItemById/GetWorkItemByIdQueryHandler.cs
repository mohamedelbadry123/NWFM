namespace Workflow.Application.Queries.GetWorkItemById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkItemByIdQueryHandler
    : IRequestHandler<GetWorkItemByIdQuery, Result<WorkItemDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _repo;
    private readonly IWorkItemDtoAssembler _assembler;

    public GetWorkItemByIdQueryHandler(
        IWorkflowFeatureGate gate, IWorkItemRepository repo, IWorkItemDtoAssembler assembler)
    {
        _gate = gate;
        _repo = repo;
        _assembler = assembler;
    }

    public async Task<Result<WorkItemDto>> Handle(
        GetWorkItemByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkItemDto>(gateResult.Error);

        var item = await _repo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

        return Result.Success(await _assembler.ToDtoAsync(item, includeOutcomes: true, cancellationToken));
    }
}
