namespace Workflow.Application.Queries.ListFailedOutbox;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListFailedOutboxQuery(
    Guid OrganizationId,
    int PageNumber = 1,
    int PageSize = 20)
    : IRequest<Result<PaginatedResult<WorkflowIntegrationOutboxMessageDto>>>;
