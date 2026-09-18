namespace Workflow.Application.Queries.ListGroupInboxWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListGroupInboxWorkItemsQueryHandler
    : IRequestHandler<ListGroupInboxWorkItemsQuery, Result<IReadOnlyList<WorkItemDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkItemDtoAssembler _assembler;

    public ListGroupInboxWorkItemsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkItemDtoAssembler assembler)
    {
        _gate        = gate;
        _workItemRepo = workItemRepo;
        _groupRepo   = groupRepo;
        _assembler   = assembler;
    }

    public async Task<Result<IReadOnlyList<WorkItemDto>>> Handle(
        ListGroupInboxWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<IReadOnlyList<WorkItemDto>>(gateResult.Error);

        var group = await _groupRepo.GetByIdAsync(
            request.AssignmentGroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<IReadOnlyList<WorkItemDto>>(WorkflowErrors.AssignmentGroup.NotFound);

        var items = await _workItemRepo.GetPendingByGroupAsync(
            request.OrganizationId, request.AssignmentGroupId, cancellationToken);

        return Result.Success(await _assembler.ToDtoListAsync(items, cancellationToken));
    }
}
