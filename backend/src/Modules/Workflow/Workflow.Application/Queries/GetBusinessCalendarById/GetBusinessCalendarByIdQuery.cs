namespace Workflow.Application.Queries.GetBusinessCalendarById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetBusinessCalendarByIdQuery(Guid Id) : IRequest<Result<BusinessCalendarDto>>;
