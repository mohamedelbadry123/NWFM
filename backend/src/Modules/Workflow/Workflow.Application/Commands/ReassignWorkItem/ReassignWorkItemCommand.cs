namespace Workflow.Application.Commands.ReassignWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ReassignWorkItemCommand(
    Guid WorkItemId,
    Guid OrganizationId,
    Guid ActorUserId,
    Guid NewAssignmentGroupId) : IRequest<Result<WorkItemDto>>;
