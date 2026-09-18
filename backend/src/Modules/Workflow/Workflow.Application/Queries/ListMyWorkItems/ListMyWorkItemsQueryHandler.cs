namespace Workflow.Application.Queries.ListMyWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListMyWorkItemsQueryHandler
    : IRequestHandler<ListMyWorkItemsQuery, Result<IReadOnlyList<WorkItemDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _repo;
    private readonly IWorkItemDtoAssembler _assembler;

    public ListMyWorkItemsQueryHandler(
        IWorkflowFeatureGate gate, IWorkItemRepository repo, IWorkItemDtoAssembler assembler)
    {
        _gate = gate;
        _repo = repo;
        _assembler = assembler;
    }

    public async Task<Result<IReadOnlyList<WorkItemDto>>> Handle(
        ListMyWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<IReadOnlyList<WorkItemDto>>(gateResult.Error);

        var items = await _repo.GetByClaimedUserAsync(
            request.OrganizationId, request.UserId, cancellationToken);

        return Result.Success(await _assembler.ToDtoListAsync(items, cancellationToken));
    }
}
