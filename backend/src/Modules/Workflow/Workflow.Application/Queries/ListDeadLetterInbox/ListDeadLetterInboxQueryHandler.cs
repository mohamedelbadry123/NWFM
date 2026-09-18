namespace Workflow.Application.Queries.ListDeadLetterInbox;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class ListDeadLetterInboxQueryHandler
    : IRequestHandler<ListDeadLetterInboxQuery, Result<PaginatedResult<WorkflowIntegrationInboxMessageDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIntegrationInboxRepository _repo;

    public ListDeadLetterInboxQueryHandler(IWorkflowFeatureGate gate, IWorkflowIntegrationInboxRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowIntegrationInboxMessageDto>>> Handle(
        ListDeadLetterInboxQuery request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowIntegrationInboxMessageDto>>(gate.Error);

        var (items, total) = await _repo.GetDeadLettersPagedAsync(
            request.OrganizationId, request.PageNumber, request.PageSize, cancellationToken);

        var dtos = items.Select(m => new WorkflowIntegrationInboxMessageDto(
            m.Id, m.OrganizationId, m.MessageId, m.ModuleKey, m.BusinessEntityType,
            m.BusinessEntityId, m.TriggerEvent, m.Status.ToString(), m.AttemptCount,
            m.ErrorMessage, m.CreatedAt, m.UpdatedAt)).ToList();

        return Result.Success(new PaginatedResult<WorkflowIntegrationInboxMessageDto>(
            dtos, total, request.PageNumber, request.PageSize));
    }
}
