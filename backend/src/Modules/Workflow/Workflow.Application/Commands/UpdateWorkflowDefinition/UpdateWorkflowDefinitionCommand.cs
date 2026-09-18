namespace Workflow.Application.Commands.UpdateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record UpdateWorkflowDefinitionCommand(
    Guid DefinitionId,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr) : IRequest<Result<WorkflowDefinitionDto>>;
