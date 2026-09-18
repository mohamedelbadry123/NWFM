namespace Workflow.Application.Queries.ListWorkflowInstancesByOrg;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowInstancesByOrgQueryHandler
    : IRequestHandler<ListWorkflowInstancesByOrgQuery, Result<PaginatedResult<WorkflowInstanceDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _repo;

    public ListWorkflowInstancesByOrgQueryHandler(IWorkflowFeatureGate gate, IWorkflowInstanceRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowInstanceDto>>> Handle(
        ListWorkflowInstancesByOrgQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<PaginatedResult<WorkflowInstanceDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedByOrgAsync(
            request.OrganizationId, request.PageNumber, request.PageSize, cancellationToken);

        var dtos = items.Select(i => new WorkflowInstanceDto(
            i.Id, i.OrganizationId, i.WorkflowBindingId, i.PinnedWorkflowVersionId,
            i.IdempotencyKey, i.BusinessEntityId, i.CorrelationId, i.Status,
            i.StartedAt, i.CompletedAt, i.CancelledAt, i.SuspendedAt,
            i.FailureReason, i.StartedByUserId, i.CurrentActivityNodeKey,
            i.CreatedAt, i.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowInstanceDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
