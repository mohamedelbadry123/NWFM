namespace Workflow.Application.Commands.UpdateWorkflowBinding;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record UpdateWorkflowBindingCommand(
    Guid BindingId,
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
    WorkflowExecutionPolicy? ExecutionPolicy = null) : IRequest<Result<WorkflowBindingDto>>;
