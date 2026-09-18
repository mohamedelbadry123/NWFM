namespace Workflow.Application.Commands.ReplayOutboxMessage;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ReplayOutboxMessageCommandHandler
    : IRequestHandler<ReplayOutboxMessageCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIntegrationOutboxRepository _repo;

    public ReplayOutboxMessageCommandHandler(IWorkflowFeatureGate gate, IWorkflowIntegrationOutboxRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result> Handle(ReplayOutboxMessageCommand request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure) return gate;

        var message = await _repo.GetByIdAsync(request.MessageId, cancellationToken);
        if (message is null || message.OrganizationId != request.OrganizationId)
            return Result.Failure(new Error("Workflow.Outbox.NotFound", "Outbox message was not found."));

        if (message.Status != WorkflowOutboxStatus.Failed)
            return Result.Failure(new Error("Workflow.Outbox.NotFailed", "Only failed outbox messages can be replayed."));

        message.ResetForReplay(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
