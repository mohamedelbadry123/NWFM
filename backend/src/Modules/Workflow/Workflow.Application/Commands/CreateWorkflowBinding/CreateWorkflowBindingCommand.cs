namespace Workflow.Application.Commands.CreateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record CreateWorkflowBindingCommand(
    Guid DefinitionId,
    Guid OrganizationId,
    string ModuleKey,
    string EntityType,
    string TriggerEvent,
    string? Description,
    WorkflowBindingMode Mode,
    WorkflowVersionPolicy VersionPolicy,
    Guid? FixedWorkflowVersionId,
    string? StartEventKey,
    string? StartConditionExpression,
    string? ScreenKey = null,
    string? InputMappingJson = null,
    string? OutcomeMappingJson = null,
    string? ConditionJson = null,
    WorkflowExecutionPolicy ExecutionPolicy = WorkflowExecutionPolicy.StartNewInstance) : IRequest<Result<WorkflowBindingDto>>;
