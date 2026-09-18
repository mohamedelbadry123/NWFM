namespace Workflow.Application.Commands.StartWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class StartWorkflowInstanceCommandHandler
    : IRequestHandler<StartWorkflowInstanceCommand, Result<WorkflowInstanceDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _bindingRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowRuntimeEngine _engine;

    public StartWorkflowInstanceCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository bindingRepo,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowRuntimeEngine engine)
    {
        _gate        = gate;
        _bindingRepo = bindingRepo;
        _instanceRepo = instanceRepo;
        _engine      = engine;
    }

    public async Task<Result<WorkflowInstanceDto>> Handle(
        StartWorkflowInstanceCommand request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowInstanceDto>(gateResult.Error);

        var binding = await _bindingRepo.GetByIdAsync(request.WorkflowBindingId, cancellationToken);
        if (binding is null || binding.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.Binding.NotFound);

        var duplicate = await _instanceRepo.ExistsByIdempotencyKeyAsync(
            request.OrganizationId, request.IdempotencyKey, cancellationToken);
        if (duplicate)
            return Result.Failure<WorkflowInstanceDto>(WorkflowErrors.Instance.AlreadyStarted);

        var result = await _engine.StartAsync(
            request.OrganizationId,
            request.WorkflowBindingId,
            request.BusinessEntityId,
            request.IdempotencyKey,
            DateTime.UtcNow,
            request.CorrelationId,
            request.StartedByUserId,
            cancellationToken: cancellationToken);

        if (result.IsFailure) return Result.Failure<WorkflowInstanceDto>(result.Error);

        var i = result.Value;
        return Result.Success(new WorkflowInstanceDto(
            i.Id, i.OrganizationId, i.WorkflowBindingId, i.PinnedWorkflowVersionId,
            i.IdempotencyKey, i.BusinessEntityId, i.CorrelationId, i.Status,
            i.StartedAt, i.CompletedAt, i.CancelledAt, i.SuspendedAt,
            i.FailureReason, i.StartedByUserId, i.CurrentActivityNodeKey,
            i.CreatedAt, i.UpdatedAt));
    }
}

