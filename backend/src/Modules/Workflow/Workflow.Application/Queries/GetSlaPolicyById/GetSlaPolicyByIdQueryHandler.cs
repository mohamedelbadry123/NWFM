namespace Workflow.Application.Queries.GetSlaPolicyById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class GetSlaPolicyByIdQueryHandler
    : IRequestHandler<GetSlaPolicyByIdQuery, Result<SlaPolicyDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly ISlaPolicyRepository _repo;

    public GetSlaPolicyByIdQueryHandler(IWorkflowFeatureGate gate, ISlaPolicyRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<SlaPolicyDto>> Handle(
        GetSlaPolicyByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<SlaPolicyDto>(gateResult.Error);

        var policy = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (policy is null)
            return Result.Failure<SlaPolicyDto>(WorkflowErrors.Sla.NotFound);

        return Result.Success(WorkflowOpsMappings.ToDto(policy));
    }
}
