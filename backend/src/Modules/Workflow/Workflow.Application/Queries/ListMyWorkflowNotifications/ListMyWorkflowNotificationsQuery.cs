namespace Workflow.Application.Queries.ListMyWorkflowNotifications;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

/// <summary>
/// Returns a paged list of workflow notification logs for the calling user's organization.
/// Optionally scoped to the calling user as recipient.
/// </summary>
public sealed record ListMyWorkflowNotificationsQuery(
    Guid OrganizationId,
    Guid? RecipientUserId,
    int PageNumber,
    int PageSize) : IRequest<Result<WorkflowNotificationLogPageDto>>;
