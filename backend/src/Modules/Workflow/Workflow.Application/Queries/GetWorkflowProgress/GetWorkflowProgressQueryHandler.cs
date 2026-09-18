namespace Workflow.Application.Queries.GetWorkflowProgress;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowProgressQueryHandler
    : IRequestHandler<GetWorkflowProgressQuery, Result<WorkflowProgressDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IActivityInstanceRepository _activityRepo;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IWorkflowHistoryBuilder _history;

    public GetWorkflowProgressQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IActivityInstanceRepository activityRepo,
        IWorkflowEventRepository eventRepo,
        IWorkflowHistoryBuilder history)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _activityRepo = activityRepo;
        _eventRepo    = eventRepo;
        _history      = history;
    }

    public async Task<Result<WorkflowProgressDto>> Handle(
        GetWorkflowProgressQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowProgressDto>(gateResult.Error);

        var instance = await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || instance.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowProgressDto>(WorkflowErrors.Instance.NotFound);

        var activities = await _activityRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        var events     = await _eventRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);

        var instanceDto = new WorkflowInstanceDto(
            instance.Id, instance.OrganizationId, instance.WorkflowBindingId,
            instance.PinnedWorkflowVersionId, instance.IdempotencyKey,
            instance.BusinessEntityId, instance.CorrelationId, instance.Status,
            instance.StartedAt, instance.CompletedAt, instance.CancelledAt,
            instance.SuspendedAt, instance.FailureReason, instance.StartedByUserId,
            instance.CurrentActivityNodeKey, instance.CreatedAt, instance.UpdatedAt);

        var eventDtos = await _history.BuildAsync(
            instance, events, activities, crossTenant: false, cancellationToken);
        var activityDtos = WorkflowHistoryComposer.RelabelActivities(activities, eventDtos);

        return Result.Success(new WorkflowProgressDto(instanceDto, activityDtos, eventDtos));
    }
}
