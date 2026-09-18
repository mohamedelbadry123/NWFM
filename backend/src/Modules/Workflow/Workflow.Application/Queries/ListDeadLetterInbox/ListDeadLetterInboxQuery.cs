namespace Workflow.Application.Queries.ListDeadLetterInbox;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListDeadLetterInboxQuery(
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = 20)
    : IRequest<Result<PaginatedResult<WorkflowIntegrationInboxMessageDto>>>;
