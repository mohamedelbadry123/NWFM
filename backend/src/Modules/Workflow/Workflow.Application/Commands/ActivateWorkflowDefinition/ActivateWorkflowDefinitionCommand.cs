namespace Workflow.Application.Commands.ActivateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record ActivateWorkflowDefinitionCommand(
    Guid DefinitionId) : IRequest<Result<WorkflowDefinitionDto>>;
