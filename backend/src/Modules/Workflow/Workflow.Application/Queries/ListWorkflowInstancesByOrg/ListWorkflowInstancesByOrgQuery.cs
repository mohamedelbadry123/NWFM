namespace Workflow.Application.Queries.ListWorkflowInstancesByOrg;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowInstancesByOrgQuery(
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<Result<PaginatedResult<WorkflowInstanceDto>>>;
