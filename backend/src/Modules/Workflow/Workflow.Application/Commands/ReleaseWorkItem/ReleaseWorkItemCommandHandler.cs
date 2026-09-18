namespace Workflow.Application.Commands.ReleaseWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ReleaseWorkItemCommandHandler
    : IRequestHandler<ReleaseWorkItemCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowRequestProjector _projector;

    public ReleaseWorkItemCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkItemRepository workItemRepo,
        IWorkflowRequestProjector projector)
    {
        _gate        = gate;
        _workItemRepo = workItemRepo;
        _projector   = projector;
    }

    public async Task<Result<bool>> Handle(ReleaseWorkItemCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        var item = await _workItemRepo.GetByIdAsync(request.WorkItemId, cancellationToken);
        if (item is null || item.OrganizationId != request.OrganizationId)
            return Result.Failure<bool>(WorkflowErrors.WorkItem.NotFound);

        if (item.Status != WorkItemStatus.Claimed)
            return Result.Failure<bool>(WorkflowErrors.WorkItem.NotClaimed);

        if (item.ClaimedByUserId != request.UserId)
            return Result.Failure<bool>(WorkflowErrors.WorkItem.NotClaimedByUser);

        var now = DateTime.UtcNow;
        item.Release(now);
        await _workItemRepo.SaveChangesAsync(cancellationToken);
        await _projector.SyncClaimAsync(
            item.WorkflowInstanceId, request.OrganizationId, null, now, cancellationToken);
        return Result.Success(true);
    }
}
