namespace Workflow.Application.Queries.ListWorkflowIncidents;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class ListWorkflowIncidentsQueryHandler
    : IRequestHandler<ListWorkflowIncidentsQuery, Result<PaginatedResult<WorkflowIncidentDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIncidentRepository _repo;

    public ListWorkflowIncidentsQueryHandler(IWorkflowFeatureGate gate, IWorkflowIncidentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<WorkflowIncidentDto>>> Handle(
        ListWorkflowIncidentsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<WorkflowIncidentDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.PageNumber, request.PageSize,
            request.OrganizationId, request.Status, cancellationToken);

        return Result.Success(new PaginatedResult<WorkflowIncidentDto>(
            items.Select(WorkflowOpsMappings.ToDto).ToList(),
            total, request.PageNumber, request.PageSize));
    }
}
