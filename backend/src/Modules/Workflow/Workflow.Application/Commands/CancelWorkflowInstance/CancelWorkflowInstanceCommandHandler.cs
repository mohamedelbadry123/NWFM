namespace Workflow.Application.Commands.CancelWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class CancelWorkflowInstanceCommandHandler
    : IRequestHandler<CancelWorkflowInstanceCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkItemRepository _workItemRepo;
    private readonly IWorkflowTimerRepository _timerRepo;
    private readonly IWorkflowExecutionTokenRepository _tokenRepo;
    private readonly IWorkflowEventAppender _events;

    public CancelWorkflowInstanceCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IWorkItemRepository workItemRepo,
        IWorkflowTimerRepository timerRepo,
        IWorkflowExecutionTokenRepository tokenRepo,
        IWorkflowEventAppender events)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _workItemRepo = workItemRepo;
        _timerRepo    = timerRepo;
        _tokenRepo    = tokenRepo;
        _events       = events;
    }

    public async Task<Result<bool>> Handle(
        CancelWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        var instance = await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || instance.OrganizationId != request.OrganizationId)
            return Result.Failure<bool>(WorkflowErrors.Instance.NotFound);

        if (instance.Status is not (WorkflowInstanceStatus.Running or WorkflowInstanceStatus.Suspended))
            return Result.Failure<bool>(WorkflowErrors.Instance.NotRunning);

        var now = DateTime.UtcNow;
        instance.Cancel(now);

        var items = await _workItemRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        foreach (var item in items.Where(w => w.Status is WorkItemStatus.Pending or WorkItemStatus.Claimed))
            item.Cancel(now);

        var timers = await _timerRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        foreach (var timer in timers.Where(t => t.Status is WorkflowTimerStatus.Pending or WorkflowTimerStatus.Fired))
            timer.Cancel(now);

        var tokens = await _tokenRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        foreach (var token in tokens.Where(t => t.Status == ExecutionTokenStatus.Active))
            token.Cancel(now);

        await _timerRepo.SaveChangesAsync(cancellationToken);
        await _tokenRepo.SaveChangesAsync(cancellationToken);

        await _events.AppendAsync(
            request.OrganizationId, request.InstanceId,
            WorkflowEventType.InstanceCancelled, now,
            actorUserId: request.ActorUserId, cancellationToken: cancellationToken);

        return Result.Success(true);
    }
}
