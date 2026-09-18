namespace Workflow.Application.Commands.ReassignWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ReassignWorkItemCommandHandler
    : IRequestHandler<ReassignWorkItemCommand, Result<WorkItemDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowCandidateFactory _candidateFactory;
    private readonly IWorkItemCandidateRepository _candidateRepo;
    private readonly IWorkflowEventAppender _events;
    private readonly IWorkflowRequestProjector _projector;

    public ReassignWorkItemCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowCandidateFactory candidateFactory,
        IWorkItemCandidateRepository candidateRepo,
        IWorkflowEventAppender events,
        IWorkflowRequestProjector projector)
    {
        _gate             = gate;
        _workItemRepo     = workItemRepo;
        _groupRepo        = groupRepo;
        _candidateFactory = candidateFactory;
        _candidateRepo    = candidateRepo;
        _events           = events;
        _projector        = projector;
    }

    public async Task<Result<WorkItemDto>> Handle(
        ReassignWorkItemCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkItemDto>(gateResult.Error);

        var item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.NotFound);

        if (item.Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled)
            return Result.Failure<WorkItemDto>(WorkflowErrors.WorkItem.AlreadyCompleted);

        var group = await _groupRepo.GetByIdAsync(request.NewAssignmentGroupId, request.OrganizationId, cancellationToken);
        if (group is null || !group.IsActive)
            return Result.Failure<WorkItemDto>(WorkflowErrors.AssignmentGroup.NotFound);

        var now = DateTime.UtcNow;
        item.Reassign(request.NewAssignmentGroupId, now);
        await _workItemRepo.SaveChangesAsync(cancellationToken);

        var candidates = await _candidateFactory.CreateCandidatesAsync(
            request.OrganizationId, item.Id, request.NewAssignmentGroupId, now, cancellationToken);
        await _candidateRepo.AddRangeAsync(candidates, cancellationToken);

        await _events.AppendAsync(
            request.OrganizationId, item.WorkflowInstanceId,
            WorkflowEventType.WorkItemReassigned, now,
            actorUserId: request.ActorUserId,
            payloadJson: $"{{\"workItemId\":\"{item.Id}\",\"newGroupId\":\"{request.NewAssignmentGroupId}\"}}",
            cancellationToken: cancellationToken);
        await _projector.SyncAssignmentAsync(
            item.WorkflowInstanceId, request.OrganizationId, request.NewAssignmentGroupId,
            null, now, cancellationToken);

        return Result.Success(ClaimWorkItemCommandHandler.MapToDto(item));
    }
}
