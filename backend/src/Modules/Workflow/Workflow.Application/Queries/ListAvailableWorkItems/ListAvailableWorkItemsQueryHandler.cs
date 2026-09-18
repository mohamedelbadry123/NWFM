namespace Workflow.Application.Queries.ListAvailableWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListAvailableWorkItemsQueryHandler
    : IRequestHandler<ListAvailableWorkItemsQuery, Result<IReadOnlyList<WorkItemDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItems;
    private readonly IWorkflowAssignmentGroupRepository _groups;
    private readonly IWorkflowParticipantRepository _participants;
    private readonly IWorkItemDtoAssembler _assembler;

    public ListAvailableWorkItemsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItems,
        IWorkflowAssignmentGroupRepository groups,
        IWorkflowParticipantRepository participants,
        IWorkItemDtoAssembler assembler)
    {
        _gate = gate;
        _workItems = workItems;
        _groups = groups;
        _participants = participants;
        _assembler = assembler;
    }

    public async Task<Result<IReadOnlyList<WorkItemDto>>> Handle(
        ListAvailableWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<IReadOnlyList<WorkItemDto>>(gateResult.Error);

        var groupIds = await _groups.GetGroupIdsForUserAsync(
            request.UserId, request.OrganizationId, cancellationToken);
        if (groupIds.Count > 0)
        {
            var mine = await _workItems.GetPendingForGroupIdsAsync(
                request.OrganizationId, groupIds, cancellationToken);
            return Result.Success(await _assembler.ToDtoListAsync(mine, cancellationToken));
        }

        // Org owners are often not registered as participants. Show every pending
        // item in the tenant so they can see work exists. Claim still requires
        // being an active member of the work item's assignment group.
        var isParticipant = await _participants.ExistsActiveForUserAsync(
            request.UserId, request.OrganizationId, cancellationToken);
        if (!isParticipant)
        {
            var orgPending = await _workItems.GetPendingForOrganizationAsync(
                request.OrganizationId, cancellationToken);
            return Result.Success(await _assembler.ToDtoListAsync(orgPending, cancellationToken));
        }

        return Result.Success(await _assembler.ToDtoListAsync([], cancellationToken));
    }
}
