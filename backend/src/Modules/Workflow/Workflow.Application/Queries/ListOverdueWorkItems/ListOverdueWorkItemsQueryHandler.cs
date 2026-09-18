namespace Workflow.Application.Queries.ListOverdueWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed record ListOverdueWorkItemsQuery(Guid UserId, Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<WorkItemDto>>>;

public sealed class ListOverdueWorkItemsQueryHandler
    : IRequestHandler<ListOverdueWorkItemsQuery, Result<IReadOnlyList<WorkItemDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItems;
    private readonly IWorkflowAssignmentGroupRepository _groups;
    private readonly IWorkflowParticipantRepository _participants;
    private readonly IWorkItemDtoAssembler _assembler;

    public ListOverdueWorkItemsQueryHandler(
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
        ListOverdueWorkItemsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<IReadOnlyList<WorkItemDto>>(gateResult.Error);

        var groupIds = await _groups.GetGroupIdsForUserAsync(
            request.UserId, request.OrganizationId, cancellationToken);
        if (groupIds.Count > 0)
        {
            var mine = await _workItems.GetOverdueForUserAsync(
                request.OrganizationId, request.UserId, groupIds, DateTime.UtcNow, cancellationToken);
            return Result.Success(await _assembler.ToDtoListAsync(mine, cancellationToken));
        }

        var isParticipant = await _participants.ExistsActiveForUserAsync(
            request.UserId, request.OrganizationId, cancellationToken);
        if (!isParticipant)
        {
            var orgOverdue = await _workItems.GetOverdueForOrganizationAsync(
                request.OrganizationId, DateTime.UtcNow, cancellationToken);
            return Result.Success(await _assembler.ToDtoListAsync(orgOverdue, cancellationToken));
        }

        return Result.Success(await _assembler.ToDtoListAsync([], cancellationToken));
    }
}
