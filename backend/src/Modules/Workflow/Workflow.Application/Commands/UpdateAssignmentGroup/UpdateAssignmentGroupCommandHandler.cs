namespace Workflow.Application.Commands.UpdateAssignmentGroup;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class UpdateAssignmentGroupCommandHandler
    : IRequestHandler<UpdateAssignmentGroupCommand, Result<WorkflowAssignmentGroupDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public UpdateAssignmentGroupCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowAssignmentGroupDto>> Handle(
        UpdateAssignmentGroupCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowAssignmentGroupDto>(gateResult.Error);

        var group = await _repo.GetByIdWithMembersAsync(
            request.GroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<WorkflowAssignmentGroupDto>(WorkflowErrors.AssignmentGroup.NotFound);

        var code = request.Code.Trim().ToUpperInvariant();
        var codeTaken = await _repo.CodeExistsAsync(
            code, request.OrganizationId, request.GroupId, cancellationToken);
        if (codeTaken)
            return Result.Failure<WorkflowAssignmentGroupDto>(WorkflowErrors.AssignmentGroup.DuplicateCode);

        group.Update(request.Name, request.NameAr, request.AssignmentStrategy, DateTime.UtcNow, code);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkflowAssignmentGroupDto(
            group.Id, group.OrganizationId, group.Code, group.Name, group.NameAr,
            group.AssignmentStrategy, group.IsActive,
            group.Members.Count, group.CreatedAt, group.UpdatedAt));
    }
}
