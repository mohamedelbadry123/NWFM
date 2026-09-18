namespace Workflow.Application.Commands.DeactivateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record DeactivateWorkflowDefinitionCommand(
    Guid DefinitionId) : IRequest<Result<WorkflowDefinitionDto>>;
