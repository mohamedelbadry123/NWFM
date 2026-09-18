namespace Workflow.Application.Queries.GetWorkflowTimeline;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowTimelineQueryHandler
    : IRequestHandler<GetWorkflowTimelineQuery, Result<IReadOnlyList<WorkflowEventDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowEventRepository _eventRepo;
    private readonly IActivityInstanceRepository _activityRepo;
    private readonly IWorkflowHistoryBuilder _history;

    public GetWorkflowTimelineQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowEventRepository eventRepo,
        IActivityInstanceRepository activityRepo,
        IWorkflowHistoryBuilder history)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
        _eventRepo    = eventRepo;
        _activityRepo = activityRepo;
        _history      = history;
    }

    public async Task<Result<IReadOnlyList<WorkflowEventDto>>> Handle(
        GetWorkflowTimelineQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<IReadOnlyList<WorkflowEventDto>>(gateResult.Error);

        var instance = await _instanceRepo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || instance.OrganizationId != request.OrganizationId)
            return Result.Failure<IReadOnlyList<WorkflowEventDto>>(WorkflowErrors.Instance.NotFound);

        var events     = await _eventRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        var activities = await _activityRepo.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        var dtos       = await _history.BuildAsync(
            instance, events, activities, crossTenant: false, cancellationToken);

        return Result.Success(dtos);
    }
}
