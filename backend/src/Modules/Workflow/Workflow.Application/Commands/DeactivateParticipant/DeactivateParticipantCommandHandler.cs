namespace Workflow.Application.Commands.DeactivateParticipant;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Repositories;

public sealed class DeactivateParticipantCommandHandler
    : IRequestHandler<DeactivateParticipantCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowParticipantRepository _repo;

    public DeactivateParticipantCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowParticipantRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result> Handle(
        DeactivateParticipantCommand request,
        CancellationToken cancellationToken)
    {
        var gateResult = _gate.EnsureEnabled();
        if (gateResult.IsFailure) return gateResult;

        var participant = await _repo.GetByIdAsync(
            request.ParticipantId, request.OrganizationId, cancellationToken);
        if (participant is null)
            return Result.Failure(WorkflowErrors.Participant.NotFound);

        participant.Deactivate(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
