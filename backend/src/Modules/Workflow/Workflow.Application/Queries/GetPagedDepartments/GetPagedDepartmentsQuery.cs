namespace Workflow.Application.Queries.GetPagedDepartments;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetPagedDepartmentsQuery(
    Guid OrganizationId,
    int PageNumber,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PaginatedResult<WorkflowDepartmentDto>>>;
