namespace Workflow.Application.Queries.ListFailedOutbox;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListFailedOutboxQueryHandler
    : IRequestHandler<ListFailedOutboxQuery, Result<PaginatedResult<WorkflowIntegrationOutboxMessageDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIntegrationOutboxRepository _repo;

    public ListFailedOutboxQueryHandler(IWorkflowFeatureGate gate, IWorkflowIntegrationOutboxRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowIntegrationOutboxMessageDto>>> Handle(
        ListFailedOutboxQuery request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowIntegrationOutboxMessageDto>>(gate.Error);

        var (items, total) = await _repo.GetFailedOutboxPagedAsync(
            request.OrganizationId, request.PageNumber, request.PageSize, cancellationToken);

        var dtos = items.Select(m => new WorkflowIntegrationOutboxMessageDto(
            m.Id, m.OrganizationId, m.MessageId, m.ModuleKey, m.BusinessEntityType,
            m.BusinessEntityId, m.OutcomeKey, m.Status.ToString(), m.AttemptCount,
            m.CreatedAt, m.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowIntegrationOutboxMessageDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
