namespace Workflow.Application.Queries.GetSlaPolicyById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetSlaPolicyByIdQuery(Guid Id) : IRequest<Result<SlaPolicyDto>>;
