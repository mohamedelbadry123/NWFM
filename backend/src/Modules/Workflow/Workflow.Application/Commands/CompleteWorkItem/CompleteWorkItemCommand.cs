namespace Workflow.Application.Commands.CompleteWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CompleteWorkItemCommand(
    Guid WorkItemId,
    Guid UserId,
    Guid OrganizationId,
    string ActionTaken,
    string? Comment = null,
    Guid? RedirectAssignmentGroupId = null,
    Guid? RedirectDepartmentId = null) : IRequest<Result<WorkItemDto>>;
