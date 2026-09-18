namespace Workflow.Application.Queries.ListWorkflowBindingsByDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowBindingsByDefinitionQuery(
    Guid DefinitionId,
    int PageNumber,
    int PageSize) : IRequest<Result<PaginatedResult<WorkflowBindingDto>>>;
