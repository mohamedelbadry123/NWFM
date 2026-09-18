namespace Workflow.Application.Queries.GetWorkflowIncidentById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class GetWorkflowIncidentByIdQueryHandler
    : IRequestHandler<GetWorkflowIncidentByIdQuery, Result<WorkflowIncidentDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIncidentRepository _repo;

    public GetWorkflowIncidentByIdQueryHandler(IWorkflowFeatureGate gate, IWorkflowIncidentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowIncidentDto>> Handle(
        GetWorkflowIncidentByIdQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowIncidentDto>(gateResult.Error);

        var incident = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (incident is null)
            return Result.Failure<WorkflowIncidentDto>(WorkflowErrors.Incident.NotFound);

        return Result.Success(WorkflowOpsMappings.ToDto(incident));
    }
}
