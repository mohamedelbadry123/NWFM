namespace Workflow.Application.Queries.ListSlaPolicies;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class ListSlaPoliciesQueryHandler
    : IRequestHandler<ListSlaPoliciesQuery, Result<PaginatedResult<SlaPolicyDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly ISlaPolicyRepository _repo;

    public ListSlaPoliciesQueryHandler(IWorkflowFeatureGate gate, ISlaPolicyRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<PaginatedResult<SlaPolicyDto>>> Handle(
        ListSlaPoliciesQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<PaginatedResult<SlaPolicyDto>>(gateResult.Error);

        var (items, total) = await _repo.GetPagedAsync(
            request.PageNumber, request.PageSize, request.SearchTerm, cancellationToken);

        return Result.Success(new PaginatedResult<SlaPolicyDto>(
            items.Select(WorkflowOpsMappings.ToDto).ToList(),
            total, request.PageNumber, request.PageSize));
    }
}
