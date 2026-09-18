namespace Workflow.Application.Queries.ListBusinessCalendars;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class ListBusinessCalendarsQueryHandler
    : IRequestHandler<ListBusinessCalendarsQuery, Result<PaginatedResult<BusinessCalendarDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public ListBusinessCalendarsQueryHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<BusinessCalendarDto>>> Handle(
        ListBusinessCalendarsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<BusinessCalendarDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.PageNumber, request.PageSize, request.SearchTerm, cancellationToken);

        return Result.Success(new PaginatedResult<BusinessCalendarDto>(
            items.Select(c => WorkflowOpsMappings.ToDto(c)).ToList(),
            total, request.PageNumber, request.PageSize));
    }
}
