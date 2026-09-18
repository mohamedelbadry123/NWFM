namespace Workflow.Application.Queries.GetPagedParticipants;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetPagedParticipantsQuery(
    Guid OrganizationId,
    int PageNumber,
    int PageSize,
    string? SearchTerm) : IRequest<Result<PaginatedResult<WorkflowParticipantDto>>>;
