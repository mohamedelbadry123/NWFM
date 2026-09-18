namespace Workflow.Application.Commands.UpdateWorkflowBindingAssignmentMapping;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record UpdateWorkflowBindingAssignmentMappingCommand(
    Guid MappingId,
    Guid OrganizationId,
    Guid AssignmentGroupId) : IRequest<Result<WorkflowBindingAssignmentMappingDto>>;
