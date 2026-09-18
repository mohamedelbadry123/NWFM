namespace Workflow.Application.Commands.ClaimWorkItem;

using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ClaimWorkItemCommandHandler
    : IRequestHandler<ClaimWorkItemCommand, Result<WorkItemDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowParticipantRepository _participantRepo;
    private readonly IWorkflowEventAppender _events;
    private readonly IWorkItemDtoAssembler _assembler;
    private readonly IWorkflowRequestProjector _projector;

    public ClaimWorkItemCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowParticipantRepository participantRepo,
        IWorkflowEventAppender events,
        IWorkItemDtoAssembler assembler,
        IWorkflowRequestProjector projector)
    {
        _gate            = gate;
        _workItemRepo    = workItemRepo;
        _groupRepo       = groupRepo;
        _participantRepo = participantRepo;
        _events          = events;
        _assembler       = assembler;
        _projector       = projector;
    }

    public async Task<Result<WorkItemDto>> Handle(
        ClaimWorkItemCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkItemDto>(gateResult.Error);

        var item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

        if (item.Status != Domain.Enums.WorkItemStatus.Pending)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotPending);

        var isActiveParticipant = await _participantRepo.ExistsActiveForUserAsync(
            request.UserId, request.OrganizationId, cancellationToken);
        if (!isActiveParticipant)
            return Result.Failure<WorkItemDto>(WorkflowErrors.Participant.NotFound);

        var isMember = await _groupRepo.IsUserMemberOfGroupAsync(
            item.AssignmentGroupId, request.UserId, request.OrganizationId, cancellationToken);
        if (!isMember)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotGroupMember);

        var now = DateTime.UtcNow;
        try
        {
            var claimed = await _workItemRepo.TryClaimAsync(
                item.Id, request.OrganizationId, request.UserId, now, cancellationToken);
            if (!claimed)
                return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.ConcurrencyConflict);

            item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
            if (item is null)
                return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

            await _events.AppendAsync(
                request.OrganizationId, item.WorkflowInstanceId,
                Domain.Enums.WorkflowEventType.WorkItemClaimed, now,
                actorUserId: request.UserId, cancellationToken: cancellationToken);

            await _projector.SyncClaimAsync(
                item.WorkflowInstanceId, request.OrganizationId, request.UserId, now, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.ConcurrencyConflict);
        }

        return Result.Success(await _assembler.ToDtoAsync(item, includeOutcomes: true, cancellationToken));
    }

    internal static WorkItemDto MapToDto(Domain.Entities.WorkItem item) => new(
        item.Id, item.WorkflowInstanceId, item.ActivityInstanceId, item.OrganizationId,
        item.AssignmentGroupId, null, item.ClaimedByUserId, item.ClaimedAt,
        item.CompletedByUserId, item.CompletedAt, item.DueAt, item.Status,
        item.ActionTaken, item.CommentText, item.CreatedAt, item.UpdatedAt);
}
