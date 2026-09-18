namespace Workflow.Application.Commands.RemoveGroupMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class RemoveGroupMemberCommandHandler
    : IRequestHandler<RemoveGroupMemberCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _repo;

    public RemoveGroupMemberCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result> Handle(
        RemoveGroupMemberCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return gateResult;

        var group = await _repo.GetByIdAsync(
            request.GroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure(WorkflowErrors.AssignmentGroup.NotFound);

        var member = await _repo.GetMemberAsync(
            request.GroupId, request.ParticipantId, cancellationToken);
        if (member is null)
            return Result.Failure(WorkflowErrors.AssignmentGroup.MemberNotFound);

        await _repo.RemoveMemberAsync(member, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
