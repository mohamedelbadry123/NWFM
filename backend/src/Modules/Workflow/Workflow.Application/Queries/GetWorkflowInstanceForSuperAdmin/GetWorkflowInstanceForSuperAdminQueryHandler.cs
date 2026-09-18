namespace Workflow.Application.Queries.GetWorkflowInstanceForSuperAdmin;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowInstanceForSuperAdminQueryHandler
    : IRequestHandler<GetWorkflowInstanceForSuperAdminQuery, Result<WorkflowProgressDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IActivityInstanceRepository _activityRepo;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IWorkflowHistoryBuilder _history;

    public GetWorkflowInstanceForSuperAdminQueryHandler(
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
        GetWorkflowInstanceForSuperAdminQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowProgressDto>(gateResult.Error);

        // IgnoreQueryFilters used via GetByIdForSuperAdminAsync — SuperAdmin cross-tenant read
        var instance = await _instanceRepo.GetByIdForSuperAdminAsync(request.InstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure<WorkflowProgressDto>(WorkflowErrors.Instance.NotFound);

        // IgnoreQueryFilters via GetByInstanceIdForSuperAdminAsync — SuperAdmin cross-tenant read
        var activities = await _activityRepo.GetByInstanceIdForSuperAdminAsync(request.InstanceId, cancellationToken);
        var events     = await _eventRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);

        var instanceDto = new WorkflowInstanceDto(
            instance.Id, instance.OrganizationId, instance.WorkflowBindingId,
            instance.PinnedWorkflowVersionId, instance.IdempotencyKey,
            instance.BusinessEntityId, instance.CorrelationId, instance.Status,
            instance.StartedAt, instance.CompletedAt, instance.CancelledAt,
            instance.SuspendedAt, instance.FailureReason, instance.StartedByUserId,
            instance.CurrentActivityNodeKey, instance.CreatedAt, instance.UpdatedAt);

        var eventDtos = await _history.BuildAsync(
            instance, events, activities, crossTenant: true, cancellationToken);
        var activityDtos = WorkflowHistoryComposer.RelabelActivities(activities, eventDtos);

        return Result.Success(new WorkflowProgressDto(instanceDto, activityDtos, eventDtos));
    }
}
