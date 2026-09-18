namespace Workflow.Application.Commands.CreateWorkflowDepartment;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class CreateWorkflowDepartmentCommandHandler
    : IRequestHandler<CreateWorkflowDepartmentCommand, Result<WorkflowDepartmentDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDepartmentRepository _repo;

    public CreateWorkflowDepartmentCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDepartmentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowDepartmentDto>> Handle(
        CreateWorkflowDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDepartmentDto>(gateResult.Error);

        var now = DateTime.UtcNow;
        var department = WorkflowDepartment.Create(
            organizationId: request.OrganizationId,
            name: request.Name,
            createdAt: now,
            nameAr: request.NameAr,
            code: request.Code,
            defaultAssignmentGroupId: request.DefaultAssignmentGroupId);

        await _repo.AddAsync(department, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkflowDepartmentDto(
            department.Id, department.OrganizationId, department.Name, department.NameAr,
            department.Code, department.DefaultAssignmentGroupId, department.IsActive,
            MemberCount: 0, department.CreatedAt, department.UpdatedAt));
    }
}
