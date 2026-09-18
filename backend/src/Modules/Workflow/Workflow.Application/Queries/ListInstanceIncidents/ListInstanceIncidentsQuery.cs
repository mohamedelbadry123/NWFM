namespace Workflow.Application.Queries.ListInstanceIncidents;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListInstanceIncidentsQuery(
    Guid InstanceId,
    Guid OrganizationId,
    bool BypassTenantCheck = false) : IRequest<Result<IReadOnlyList<WorkflowIncidentSummaryDto>>>;
