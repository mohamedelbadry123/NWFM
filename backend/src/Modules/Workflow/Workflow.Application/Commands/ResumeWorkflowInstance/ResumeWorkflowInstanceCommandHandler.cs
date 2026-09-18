namespace Workflow.Application.Commands.ResumeWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ResumeWorkflowInstanceCommandHandler
    : IRequestHandler<ResumeWorkflowInstanceCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowEventAppender _events;

    public ResumeWorkflowInstanceCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowEventAppender events)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _events       = events;
    }

    public async Task<Result<bool>> Handle(
        ResumeWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        var instance = request.OrganizationId == Guid.Empty
            ? await _instanceRepo.GetByIdForSuperAdminAsync(request.InstanceId, cancellationToken)
            : await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || (request.OrganizationId != Guid.Empty && instance.OrganizationId != request.OrganizationId))
            return Result.Failure<bool>(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Suspended)
            return Result.Failure<bool>(WorkflowErrors.Instance.NotSuspended);

        var now = DateTime.UtcNow;
        instance.Resume(now);

        await _events.AppendAsync(
            instance.OrganizationId, request.InstanceId,
            WorkflowEventType.InstanceResumed, now,
            actorUserId: request.ActorUserId, cancellationToken: cancellationToken);

        return Result.Success(true);
    }
}
