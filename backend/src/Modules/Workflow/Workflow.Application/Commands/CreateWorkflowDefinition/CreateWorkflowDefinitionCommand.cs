namespace Workflow.Application.Commands.CreateWorkflowDefinition;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CreateWorkflowDefinitionCommand(
    Guid OrganizationId,
    string DefinitionKey,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr) : IRequest<Result<WorkflowDefinitionDto>>;
