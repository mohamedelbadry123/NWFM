namespace Workflow.Application.Queries.ListWorkflowVersions;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowVersionsQuery(
    Guid DefinitionId,
    int PageNumber,
    int PageSize) : IRequest<Result<PaginatedResult<WorkflowVersionDto>>>;
