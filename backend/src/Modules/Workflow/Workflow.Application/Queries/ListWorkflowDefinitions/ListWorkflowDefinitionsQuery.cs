namespace Workflow.Application.Queries.ListWorkflowDefinitions;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListWorkflowDefinitionsQuery(
    int PageNumber,
    int PageSize,
    string? SearchTerm,
    Guid? OrganizationId) : IRequest<Result<PaginatedResult<WorkflowDefinitionDto>>>;
