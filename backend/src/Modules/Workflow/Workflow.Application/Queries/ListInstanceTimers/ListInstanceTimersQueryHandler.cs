namespace Workflow.Application.Queries.ListInstanceTimers;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class ListInstanceTimersQueryHandler
    : IRequestHandler<ListInstanceTimersQuery, Result<IReadOnlyList<WorkflowTimerDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowTimerRepository _timers;

    public ListInstanceTimersQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instances,
        IWorkflowTimerRepository timers)
    {
        _gate = gate;
        _instances = instances;
        _timers = timers;
    }

    public async Task<Result<IReadOnlyList<WorkflowTimerDto>>> Handle(
        ListInstanceTimersQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<IReadOnlyList<WorkflowTimerDto>>(gateResult.Error);

        var instance = await _instances.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure<IReadOnlyList<WorkflowTimerDto>>(WorkflowErrors.Instance.NotFound);

        if (!request.BypassTenantCheck
            && request.OrganizationId != Guid.Empty
            && instance.OrganizationId != request.OrganizationId)
            return Result.Failure<IReadOnlyList<WorkflowTimerDto>>(WorkflowErrors.Instance.NotFound);

        var timers = await _timers.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        return Result.Success<IReadOnlyList<WorkflowTimerDto>>(
            timers.Select(WorkflowOpsMappings.ToDto).ToList());
    }
}
