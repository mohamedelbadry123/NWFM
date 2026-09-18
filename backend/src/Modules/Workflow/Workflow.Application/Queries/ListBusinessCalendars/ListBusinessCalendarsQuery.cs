namespace Workflow.Application.Queries.ListBusinessCalendars;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListBusinessCalendarsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null) : IRequest<Result<PaginatedResult<BusinessCalendarDto>>>;
