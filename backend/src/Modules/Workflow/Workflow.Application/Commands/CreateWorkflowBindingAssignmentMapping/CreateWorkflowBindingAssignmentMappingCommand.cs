namespace Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CreateWorkflowBindingAssignmentMappingCommand(
    Guid BindingId,
    Guid OrganizationId,
    string AssignmentKey,
    Guid AssignmentGroupId) : IRequest<Result<WorkflowBindingAssignmentMappingDto>>;
