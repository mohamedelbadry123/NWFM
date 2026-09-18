namespace Workflow.Application.Queries.GetWorkflowRequestById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.ListWorkflowRequests;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowRequestByIdQueryHandler
    : IRequestHandler<GetWorkflowRequestByIdQuery, Result<WorkflowRequestDto>>,
      IRequestHandler<GetWorkflowRequestByInstanceIdQuery, Result<WorkflowRequestDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowRequestRepository _repo;
    private readonly IWorkflowAssignmentGroupRepository _groups;

    public GetWorkflowRequestByIdQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowRequestRepository repo,
        IWorkflowAssignmentGroupRepository groups)
    {
        _gate = gate;
        _repo = repo;
        _groups = groups;
    }

    public async Task<Result<WorkflowRequestDto>> Handle(
        GetWorkflowRequestByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowRequestDto>(gateResult.Error);

        var entity = await _repo.GetByIdAsync(request.RequestId, request.OrganizationId, cancellationToken);
        if (entity is null)
            return Result.Failure<WorkflowRequestDto>(WorkflowErrors.Request.NotFound);

        return Result.Success(await MapAsync(entity, request.OrganizationId, cancellationToken));
    }

    public async Task<Result<WorkflowRequestDto>> Handle(
        GetWorkflowRequestByInstanceIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowRequestDto>(gateResult.Error);

        var entity = await _repo.GetByInstanceIdAsync(request.InstanceId, request.OrganizationId, cancellationToken);
        if (entity is null)
            return Result.Failure<WorkflowRequestDto>(WorkflowErrors.Request.NotFound);

        return Result.Success(await MapAsync(entity, request.OrganizationId, cancellationToken));
    }

    private async Task<WorkflowRequestDto> MapAsync(
        Domain.Entities.WorkflowRequest entity, Guid organizationId, CancellationToken cancellationToken)
    {
        var ids = new[] { entity.OriginalAssignedGroupId, entity.CurrentAssignedGroupId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var names = await _groups.GetNamesByIdsAsync(organizationId, ids, cancellationToken);
        return ListWorkflowRequestsQueryHandler.Map(entity, names);
    }
}
