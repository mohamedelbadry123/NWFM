namespace Workflow.Application.Commands.UpdateWorkflowDepartment;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class UpdateWorkflowDepartmentCommandHandler
    : IRequestHandler<UpdateWorkflowDepartmentCommand, Result<WorkflowDepartmentDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDepartmentRepository _repo;

    public UpdateWorkflowDepartmentCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDepartmentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowDepartmentDto>> Handle(
        UpdateWorkflowDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDepartmentDto>(gateResult.Error);

        var dept = await _repo.GetByIdWithMembersAsync(
            request.DepartmentId, request.OrganizationId, cancellationToken);
        if (dept is null)
            return Result.Failure<WorkflowDepartmentDto>(WorkflowErrors.Department.NotFound);

        dept.Update(request.Name, request.NameAr, request.Code,
            request.DefaultAssignmentGroupId, DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkflowDepartmentDto(
            dept.Id, dept.OrganizationId, dept.Name, dept.NameAr,
            dept.Code, dept.DefaultAssignmentGroupId, dept.IsActive,
            dept.Members.Count, dept.CreatedAt, dept.UpdatedAt));
    }
}
