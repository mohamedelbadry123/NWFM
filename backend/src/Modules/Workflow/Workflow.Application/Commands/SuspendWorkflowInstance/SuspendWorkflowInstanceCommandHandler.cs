namespace Workflow.Application.Commands.SuspendWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class SuspendWorkflowInstanceCommandHandler
    : IRequestHandler<SuspendWorkflowInstanceCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowEventAppender _events;

    public SuspendWorkflowInstanceCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowEventAppender events)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _events       = events;
    }

    public async Task<Result<bool>> Handle(
        SuspendWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        // SuperAdmin passes Guid.Empty to bypass tenant filter
        var instance = request.OrganizationId == Guid.Empty
            ? await _instanceRepo.GetByIdForSuperAdminAsync(request.InstanceId, cancellationToken)
            : await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || (request.OrganizationId != Guid.Empty && instance.OrganizationId != request.OrganizationId))
            return Result.Failure<bool>(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure<bool>(WorkflowErrors.Instance.NotRunning);

        var now = DateTime.UtcNow;
        instance.Suspend(now);

        await _events.AppendAsync(
            instance.OrganizationId, request.InstanceId,
            WorkflowEventType.InstanceSuspended, now,
            actorUserId: request.ActorUserId, cancellationToken: cancellationToken);

        return Result.Success(true);
    }
}
