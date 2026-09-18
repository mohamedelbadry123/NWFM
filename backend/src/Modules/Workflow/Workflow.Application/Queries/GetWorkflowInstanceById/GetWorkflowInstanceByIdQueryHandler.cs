namespace Workflow.Application.Queries.GetWorkflowInstanceById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowInstanceByIdQueryHandler
    : IRequestHandler<GetWorkflowInstanceByIdQuery, Result<WorkflowInstanceDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _repo;

    public GetWorkflowInstanceByIdQueryHandler(IWorkflowFeatureGate gate, IWorkflowInstanceRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowInstanceDto>> Handle(
        GetWorkflowInstanceByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowInstanceDto>(gateResult.Error);

        var instance = await _repo.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null || instance.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.Instance.NotFound);

        return Result.Success(new WorkflowInstanceDto(
            instance.Id, instance.OrganizationId, instance.WorkflowBindingId,
            instance.PinnedWorkflowVersionId, instance.IdempotencyKey,
            instance.BusinessEntityId, instance.CorrelationId, instance.Status,
            instance.StartedAt, instance.CompletedAt, instance.CancelledAt,
            instance.SuspendedAt, instance.FailureReason, instance.StartedByUserId,
            instance.CurrentActivityNodeKey, instance.CreatedAt, instance.UpdatedAt));
    }
}
