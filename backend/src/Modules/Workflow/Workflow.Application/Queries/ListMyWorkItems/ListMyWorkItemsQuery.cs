namespace Workflow.Application.Queries.ListMyWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListMyWorkItemsQuery(Guid UserId, Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<WorkItemDto>>>;
