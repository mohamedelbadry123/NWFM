namespace Workflow.Application.Queries.GetBusinessCalendarById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class GetBusinessCalendarByIdQueryHandler
    : IRequestHandler<GetBusinessCalendarByIdQuery, Result<BusinessCalendarDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IBusinessCalendarRepository _repo;

    public GetBusinessCalendarByIdQueryHandler(IWorkflowFeatureGate gate, IBusinessCalendarRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<BusinessCalendarDto>> Handle(
        GetBusinessCalendarByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<BusinessCalendarDto>(gateResult.Error);

        var calendar = await _repo.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (calendar is null)
            return Result.Failure<BusinessCalendarDto>(WorkflowErrors.Calendar.NotFound);

        return Result.Success(WorkflowOpsMappings.ToDto(calendar, includeDetails: true));
    }
}
