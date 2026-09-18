namespace Workflow.Application.Queries.ListWorkflowIncidents;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record ListWorkflowIncidentsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? OrganizationId = null,
    WorkflowIncidentStatus? Status = null) : IRequest<Result<PaginatedResult<WorkflowIncidentDto>>>;
