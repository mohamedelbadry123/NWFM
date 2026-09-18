namespace Workflow.Application.Queries.ListAllWorkflowInstances;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListAllWorkflowInstancesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? OrganizationId = null) : IRequest<Result<PaginatedResult<WorkflowInstanceDto>>>;
