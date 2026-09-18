namespace Workflow.Application.Commands.DelegateWorkItem;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

/// <summary>
/// Transfers a claimed work item from the current claimant to another group member.
/// </summary>
public sealed record DelegateWorkItemCommand(
    Guid WorkItemId,
    Guid OrganizationId,
    Guid ActorUserId,
    Guid DelegateToUserId,
    string? Comment = null) : IRequest<Result<WorkItemDto>>;
