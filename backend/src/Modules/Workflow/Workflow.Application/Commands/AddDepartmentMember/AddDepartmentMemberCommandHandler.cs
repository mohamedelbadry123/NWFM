namespace Workflow.Application.Commands.AddDepartmentMember;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class AddDepartmentMemberCommandHandler
    : IRequestHandler<AddDepartmentMemberCommand, Result<WorkflowDepartmentMemberDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowDepartmentRepository _deptRepo;
    private readonly IWorkflowParticipantRepository _participantRepo;

    public AddDepartmentMemberCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowDepartmentRepository deptRepo,
        IWorkflowParticipantRepository participantRepo)
    {
        _gate = gate;
        _deptRepo = deptRepo;
        _participantRepo = participantRepo;
    }

    public async Task<Result<WorkflowDepartmentMemberDto>> Handle(
        AddDepartmentMemberCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowDepartmentMemberDto>(gateResult.Error);

        var dept = await _deptRepo.GetByIdAsync(
            request.DepartmentId, request.OrganizationId, cancellationToken);
        if (dept is null)
            return Result.Failure<WorkflowDepartmentMemberDto>(WorkflowErrors.Department.NotFound);

        var participant = await _participantRepo.GetByIdAsync(
            request.ParticipantId, request.OrganizationId, cancellationToken);
        if (participant is null)
            return Result.Failure<WorkflowDepartmentMemberDto>(WorkflowErrors.Participant.NotFound);

        var existing = await _deptRepo.GetMemberAsync(
            request.DepartmentId, request.ParticipantId, cancellationToken);
        if (existing is not null)
            return Result.Failure<WorkflowDepartmentMemberDto>(WorkflowErrors.Department.MemberAlreadyExists);

        var member = WorkflowDepartmentMember.Create(
            request.DepartmentId, request.ParticipantId, DateTime.UtcNow);

        await _deptRepo.AddMemberAsync(member, cancellationToken);
        await _deptRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkflowDepartmentMemberDto(
            member.Id, participant.Id, participant.DisplayName,
            participant.DisplayNameAr, participant.Email));
    }
}
