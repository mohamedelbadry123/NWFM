namespace Workflow.Application.Queries.GetPagedAssignmentGroups;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetPagedAssignmentGroupsQuery(
    Guid OrganizationId,
    int PageNumber,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PaginatedResult<WorkflowAssignmentGroupDto>>>;
