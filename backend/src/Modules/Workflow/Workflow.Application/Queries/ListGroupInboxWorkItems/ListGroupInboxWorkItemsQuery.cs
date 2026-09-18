namespace Workflow.Application.Queries.ListGroupInboxWorkItems;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListGroupInboxWorkItemsQuery(Guid AssignmentGroupId, Guid OrganizationId)
    : IRequest<Result<IReadOnlyList<WorkItemDto>>>;
