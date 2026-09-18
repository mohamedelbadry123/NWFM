namespace Workflow.Application.Queries.ListAllWorkflowBindings;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ListAllWorkflowBindingsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? DefinitionId = null,
    Guid? OrganizationId = null)
    : IRequest<Result<PaginatedResult<WorkflowBindingDto>>>;
