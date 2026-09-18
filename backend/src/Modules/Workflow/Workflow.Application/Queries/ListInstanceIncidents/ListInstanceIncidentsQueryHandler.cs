namespace Workflow.Application.Queries.ListInstanceIncidents;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Application.Mapping;
using Workflow.Domain.Repositories;

public sealed class ListInstanceIncidentsQueryHandler
    : IRequestHandler<ListInstanceIncidentsQuery, Result<IReadOnlyList<WorkflowIncidentSummaryDto>>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowIncidentRepository _incidents;

    public ListInstanceIncidentsQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowInstanceRepository instances,
        IWorkflowIncidentRepository incidents)
    {
        _gate = gate;
        _instances = instances;
        _incidents = incidents;
    }

    public async Task<Result<IReadOnlyList<WorkflowIncidentSummaryDto>>> Handle(
        ListInstanceIncidentsQuery request, CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure)
            return Result.Failure<IReadOnlyList<WorkflowIncidentSummaryDto>>(gateResult.Error);

        var instance = await _instances.GetByIdAsync(request.InstanceId, cancellationToken);
        if (instance is null)
            return Result.Failure<IReadOnlyList<WorkflowIncidentSummaryDto>>(WorkflowErrors.Instance.NotFound);

        if (!request.BypassTenantCheck
            && request.OrganizationId != Guid.Empty
            && instance.OrganizationId != request.OrganizationId)
            return Result.Failure<IReadOnlyList<WorkflowIncidentSummaryDto>>(WorkflowErrors.Instance.NotFound);

        var incidents = await _incidents.GetByInstanceIdAsync(request.InstanceId, cancellationToken);
        // Safe summary DTO — no technical details / stack traces.
        return Result.Success<IReadOnlyList<WorkflowIncidentSummaryDto>>(
            incidents.Select(WorkflowOpsMappings.ToSummaryDto).ToList());
    }
}
