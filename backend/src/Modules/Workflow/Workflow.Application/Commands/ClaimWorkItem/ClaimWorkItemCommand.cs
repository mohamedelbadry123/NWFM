namespace Workflow.Application.Commands.ClaimWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ClaimWorkItemCommand(
    Guid WorkItemId,
    Guid UserId,
    Guid OrganizationId) : IRequest<Result<WorkItemDto>>;
