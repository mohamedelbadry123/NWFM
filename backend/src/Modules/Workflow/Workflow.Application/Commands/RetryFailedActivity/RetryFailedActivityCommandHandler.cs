namespace Workflow.Application.Commands.RetryFailedActivity;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class RetryFailedActivityCommandHandler
    : IRequestHandler<RetryFailedActivityCommand, Result<bool>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowRuntimeEngine _engine;
    private readonly IWorkflowEventAppender _events;

    public RetryFailedActivityCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowRuntimeEngine engine,
        IWorkflowEventAppender events)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _engine       = engine;
        _events       = events;
    }

    public async Task<Result<bool>> Handle(
        RetryFailedActivityCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<bool>(gateResult.Error);

        var instance = request.OrganizationId == Guid.Empty
            ? await _instanceRepo.GetByIdForSuperAdminAsync(request.InstanceId, cancellationToken)
            : await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || (request.OrganizationId != Guid.Empty && instance.OrganizationId != request.OrganizationId))
            return Result.Failure<bool>(WorkflowErrors.Instance.NotFound);

        if (instance.Status != WorkflowInstanceStatus.Failed)
            return Result.Failure<bool>(WorkflowErrors.Instance.NotRunning);

        var now = DateTime.UtcNow;
        // The engine validates the failed activity before changing instance status.
        var advanceResult = await _engine.AdvanceAsync(
            instance.Id, Guid.Empty, now, cancellationToken);

        if (advanceResult.IsFailure) return Result.Failure<bool>(advanceResult.Error);

        return Result.Success(true);
    }
}
