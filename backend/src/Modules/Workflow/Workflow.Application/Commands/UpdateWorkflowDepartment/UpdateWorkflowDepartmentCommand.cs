namespace Workflow.Application.Commands.UpdateWorkflowDepartment;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record UpdateWorkflowDepartmentCommand(
    Guid DepartmentId,
    Guid OrganizationId,
    string Name,
    string? NameAr,
    string? Code,
    Guid? DefaultAssignmentGroupId) : IRequest<Result<WorkflowDepartmentDto>>;
