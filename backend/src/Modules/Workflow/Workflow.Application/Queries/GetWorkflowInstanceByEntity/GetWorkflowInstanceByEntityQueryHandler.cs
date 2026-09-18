namespace Workflow.Application.Queries.GetWorkflowInstanceByEntity;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowInstanceByEntityQueryHandler
    : IRequestHandler<GetWorkflowInstanceByEntityQuery, Result<WorkflowInstanceDto?>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instanceRepo;

    public GetWorkflowInstanceByEntityQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instanceRepo)
    {
        _gate         = gate;
        _instanceRepo = instanceRepo;
    }

    public async Task<Result<WorkflowInstanceDto?>> Handle(
        GetWorkflowInstanceByEntityQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowInstanceDto?>(gateResult.Error);

        var instance = await _instanceRepo.GetByBusinessEntityAsync(
            request.OrganizationId, request.ModuleKey, request.EntityType,
            request.EntityId, cancellationToken);

        if (instance is null)
            return Result.Success<WorkflowInstanceDto?>(null);

        return Result.Success<WorkflowInstanceDto?>(new WorkflowInstanceDto(
            instance.Id, instance.OrganizationId, instance.WorkflowBindingId,
            instance.PinnedWorkflowVersionId, instance.IdempotencyKey,
            instance.BusinessEntityId, instance.CorrelationId, instance.Status,
            instance.StartedAt, instance.CompletedAt, instance.CancelledAt,
            instance.SuspendedAt, instance.FailureReason, instance.StartedByUserId,
            instance.CurrentActivityNodeKey, instance.CreatedAt, instance.UpdatedAt));
    }
}
