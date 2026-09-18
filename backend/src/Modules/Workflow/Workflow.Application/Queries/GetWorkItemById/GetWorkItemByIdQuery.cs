namespace Workflow.Application.Queries.GetWorkItemById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkItemByIdQuery(Guid WorkItemId, Guid OrganizationId)
    : IRequest<Result<WorkItemDto>>;
