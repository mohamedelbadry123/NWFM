namespace Workflow.Application.Commands.CreateAssignmentGroup;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;
using Workflow.Domain.Enums;

public sealed record CreateAssignmentGroupCommand(
    Guid OrganizationId,
    string Name,
    string? NameAr,
    AssignmentStrategy AssignmentStrategy,
    string? Code = null) : IRequest<Result<WorkflowAssignmentGroupDto>>;
