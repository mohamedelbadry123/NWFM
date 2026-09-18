namespace Workflow.Application.Commands.AddGroupMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class AddGroupMemberCommandHandler
    : IRequestHandler<AddGroupMemberCommand, Result<WorkflowGroupMemberDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkflowParticipantRepository _participantRepo;

    public AddGroupMemberCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkflowParticipantRepository participantRepo)
    {
        _gate = gate;
        _groupRepo = groupRepo;
        _participantRepo = participantRepo;
    }

    public async Task<Result<WorkflowGroupMemberDto>> Handle(
        AddGroupMemberCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowGroupMemberDto>(gateResult.Error);

        var group = await _groupRepo.GetByIdAsync(
            request.GroupId, request.OrganizationId, cancellationToken);
        if (group is null)
            return Result.Failure<WorkflowGroupMemberDto>(WorkflowErrors.AssignmentGroup.NotFound);

        var participant = await _participantRepo.GetByIdAsync(
            request.ParticipantId, request.OrganizationId, cancellationToken);
        if (participant is null)
            return Result.Failure<WorkflowGroupMemberDto>(WorkflowErrors.Participant.NotFound);

        var existing = await _groupRepo.GetMemberAsync(
            request.GroupId, request.ParticipantId, cancellationToken);
        if (existing is not null)
            return Result.Failure<WorkflowGroupMemberDto>(WorkflowErrors.AssignmentGroup.MemberAlreadyExists);

        var now = DateTime.UtcNow;
        var member = WorkflowGroupMember.Create(
            request.GroupId, request.ParticipantId,
            request.CanClaim, request.IsPrimary, now,
            request.ValidFrom, request.ValidTo);

        await _groupRepo.AddMemberAsync(member, cancellationToken);
        await _groupRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkflowGroupMemberDto(
            member.Id, participant.Id, participant.DisplayName, participant.DisplayNameAr,
            participant.Email, member.CanClaim, member.IsPrimary,
            member.ValidFrom, member.ValidTo));
    }
}
