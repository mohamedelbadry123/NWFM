namespace Workflow.Application.Commands.UpdateAssignmentGroup;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record UpdateAssignmentGroupCommand(
    Guid GroupId,
    Guid OrganizationId,
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy,
    string Code) : IRequest<Result<WorkflowAssignmentGroupDto>>;
