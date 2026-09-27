namespace Workflow.Application.Commands.RegisterParticipant;

using MediatR;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class RegisterParticipantCommandHandler
    : IRequestHandler<RegisterParticipantCommand, Result<WorkflowParticipantDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowParticipantRepository _repo;

    public RegisterParticipantCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowParticipantRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowParticipantDto>> Handle(
        RegisterParticipantCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowParticipantDto>(gateResult.Error);

        var now = DateTime.UtcNow;
        var userId = request.UserId ?? Guid.NewGuid();
        if (userId == Guid.Empty) return Result.Failure<WorkflowParticipantDto>(new Error("Workflow.Participant.InvalidUser", "A valid user identifier is required."));
        if (await _repo.ExistsByUserIdAsync(userId, request.OrganizationId, cancellationToken))
            return Result.Failure<WorkflowParticipantDto>(new Error("Workflow.Participant.AlreadyRegistered", "This authenticated user already has a participant projection."));
        var participant = WorkflowParticipant.Create(
            organizationId: request.OrganizationId,
            userId: userId,
            displayName: request.DisplayName.Trim(),
            email: request.Email.Trim(),
            createdAt: now,
            displayNameAr: request.DisplayNameAr,
            employeeNumber: request.EmployeeNumber);

        await _repo.AddAsync(participant, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(participant));
    }

    private static WorkflowParticipantDto MapToDto(WorkflowParticipant p) =>
        new(p.Id, p.OrganizationId, p.UserId, p.DisplayName, p.DisplayNameAr,
            p.Email, p.EmployeeNumber, p.IsActive, p.CreatedAt, p.UpdatedAt);
}
