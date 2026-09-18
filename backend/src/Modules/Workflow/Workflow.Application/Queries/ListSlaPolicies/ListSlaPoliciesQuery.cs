namespace Workflow.Application.Queries.ListSlaPolicies;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListSlaPoliciesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null) : IRequest<Result<PaginatedResult<SlaPolicyDto>>>;
