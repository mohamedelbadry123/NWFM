namespace Workflow.Application.Commands.CreateWorkflowDepartment;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.DTOs;

public sealed record CreateWorkflowDepartmentCommand(
    Guid OrganizationId,
    string Name,
    string? NameAr,
    string? Code,
    Guid? DefaultAssignmentGroupId) : IRequest<Result<WorkflowDepartmentDto>>;
