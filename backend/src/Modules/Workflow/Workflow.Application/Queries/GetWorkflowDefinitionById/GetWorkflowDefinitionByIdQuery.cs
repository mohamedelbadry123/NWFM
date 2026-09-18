namespace Workflow.Application.Queries.GetWorkflowDefinitionById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record GetWorkflowDefinitionByIdQuery(
    Guid DefinitionId) : IRequest<Result<WorkflowDefinitionDto>>;
