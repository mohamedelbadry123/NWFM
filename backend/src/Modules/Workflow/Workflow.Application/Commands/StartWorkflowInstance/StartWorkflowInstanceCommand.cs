namespace Workflow.Application.Commands.StartWorkflowInstance;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record StartWorkflowInstanceCommand(
    Guid OrganizationId,
    Guid WorkflowBindingId,
    string BusinessEntityId,
    string IdempotencyKey,
    string? CorrelationId = null,
    Guid? StartedByUserId = null) : IRequest<Result<WorkflowInstanceDto>>;
