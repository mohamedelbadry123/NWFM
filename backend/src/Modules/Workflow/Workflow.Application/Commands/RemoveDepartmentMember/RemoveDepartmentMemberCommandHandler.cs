namespace Workflow.Application.Commands.RemoveDepartmentMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class RemoveDepartmentMemberCommandHandler
    : IRequestHandler<RemoveDepartmentMemberCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDepartmentRepository _repo;

    public RemoveDepartmentMemberCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDepartmentRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result> Handle(
        RemoveDepartmentMemberCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return gateResult;

        var dept = await _repo.GetByIdAsync(
            request.DepartmentId, request.OrganizationId, cancellationToken);
        if (dept is null)
            return Result.Failure(WorkflowErrors.Department.NotFound);

        var member = await _repo.GetMemberAsync(
            request.DepartmentId, request.ParticipantId, cancellationToken);
        if (member is null)
            return Result.Failure(WorkflowErrors.Department.MemberNotFound);

        await _repo.RemoveMemberAsync(member, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
