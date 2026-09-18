namespace Workflow.Application.Queries.ListAvailableWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListAvailableWorkItemsQuery(Guid UserId, Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<WorkItemDto>>>;
