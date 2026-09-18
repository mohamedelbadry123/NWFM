namespace Workflow.Application.Commands.DelegateWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class DelegateWorkItemCommandHandler
    : IRequestHandler<DelegateWorkItemCommand, Result<WorkItemDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowParticipantRepository _participantRepo;
    private readonly IWorkflowEventAppender _events;
    private readonly IWorkflowRequestProjector _projector;

    public DelegateWorkItemCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowParticipantRepository participantRepo,
        IWorkflowEventAppender events,
        IWorkflowRequestProjector projector)
    {
        _gate            = gate;
        _workItemRepo    = workItemRepo;
        _groupRepo       = groupRepo;
        _participantRepo = participantRepo;
        _events          = events;
        _projector       = projector;
    }

    public async Task<Result<WorkItemDto>> Handle(
        DelegateWorkItemCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkItemDto>(gateResult.Error);

        var item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

        if (item.Status != WorkItemStatus.Claimed)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimed);

        if (item.ClaimedByUserId != request.ActorUserId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotClaimedByUser);

        var delegateIsParticipant = await _participantRepo.ExistsActiveForUserAsync(
            request.DelegateToUserId, request.OrganizationId, cancellationToken);
        if (!delegateIsParticipant)
            return Result.Failure<WorkItemDto>(WorkflowErrors.Participant.NotFound);

        var isMember = await _groupRepo.IsUserMemberOfGroupAsync(
            item.AssignmentGroupId, request.DelegateToUserId, request.OrganizationId, cancellationToken);
        if (!isMember)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotGroupMember);

        var now = DateTime.UtcNow;
        item.Delegate(request.DelegateToUserId, now);
        await _workItemRepo.SaveChangesAsync(cancellationToken);

        await _events.AppendAsync(
            request.OrganizationId, item.WorkflowInstanceId,
            WorkflowEventType.WorkItemDelegated, now,
            actorUserId: request.ActorUserId,
            payloadJson: $"{{\"workItemId\":\"{item.Id}\",\"toUserId\":\"{request.DelegateToUserId}\",\"comment\":{System.Text.Json.JsonSerializer.Serialize(request.Comment)}}}",
            cancellationToken: cancellationToken);
        await _projector.SyncAssignmentAsync(
            item.WorkflowInstanceId, request.OrganizationId, item.AssignmentGroupId,
            request.DelegateToUserId, now, cancellationToken);

        return Result.Success(ClaimWorkItemCommandHandler.MapToDto(item));
    }
}
