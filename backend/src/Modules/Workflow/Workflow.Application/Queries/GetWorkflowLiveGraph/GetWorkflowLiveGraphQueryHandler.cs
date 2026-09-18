namespace Workflow.Application.Queries.GetWorkflowLiveGraph;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Helpers;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowLiveGraphQueryHandler
    : IRequestHandler<GetWorkflowLiveGraphQuery, Result<WorkflowLiveGraphDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IActivityInstanceRepository _activities;

    public GetWorkflowLiveGraphQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instances,
        IWorkflowVersionRepository versions,
        IActivityInstanceRepository activities)
    {
        _gate = gate;
        _instances = instances;
        _versions = versions;
        _activities = activities;
    }

    public async Task<Result<WorkflowLiveGraphDto>> Handle(
        GetWorkflowLiveGraphQuery request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure) return Result.Failure<WorkflowLiveGraphDto>(gate.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync / GetByInstanceIdForSuperAdminAsync:
        // SuperAdmin live graph is a cross-tenant read of the pinned version + activity rows.
        var instance = request.SuperAdmin
            ? await _instances.GetByIdForSuperAdminAsync(request.InstanceId, cancellationToken)
            : await _instances.GetByIdAsync(request.InstanceId, cancellationToken);

        if (instance is null)
            return Result.Failure<WorkflowLiveGraphDto>(WorkflowErrors.Instance.NotFound);

        if (!request.SuperAdmin && instance.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowLiveGraphDto>(WorkflowErrors.Instance.NotFound);

        var version = await _versions.GetByIdWithProjectionAsync(
            instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return Result.Failure<WorkflowLiveGraphDto>(WorkflowErrors.Version.NotFound);

        var rows = request.SuperAdmin
            ? await _activities.GetByInstanceIdForSuperAdminAsync(instance.Id, cancellationToken)
            : await _activities.GetByInstanceIdAsync(instance.Id, cancellationToken);

        return Result.Success(WorkflowLiveGraphBuilder.Build(instance, version, rows));
    }
}
