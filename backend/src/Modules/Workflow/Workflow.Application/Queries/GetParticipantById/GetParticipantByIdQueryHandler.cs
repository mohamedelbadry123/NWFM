namespace Workflow.Application.Queries.GetParticipantById;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Repositories;

public sealed class GetParticipantByIdQueryHandler
    : IRequestHandler<GetParticipantByIdQuery, Result<WorkflowParticipantDto>>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowParticipantRepository _repo;

    public GetParticipantByIdQueryHandler(
        IWorkflowFeatureGate gate,
        IWorkflowParticipantRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result<WorkflowParticipantDto>> Handle(
        GetParticipantByIdQuery request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return Result.Failure<WorkflowParticipantDto>(gateResult.Error);

        var participant = await _repo.GetByIdAsync(
            request.ParticipantId, request.OrganizationId, cancellationToken);
        if (participant is null)
            return Result.Failure<WorkflowParticipantDto>(WorkflowErrors.Participant.NotFound);

        return Result.Success(new WorkflowParticipantDto(
            participant.Id, participant.OrganizationId, participant.UserId,
            participant.DisplayName, participant.DisplayNameAr, participant.Email,
            participant.EmployeeNumber, participant.IsActive,
            participant.CreatedAt, participant.UpdatedAt));
    }
}
