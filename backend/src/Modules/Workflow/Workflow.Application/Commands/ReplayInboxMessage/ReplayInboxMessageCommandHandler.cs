namespace Workflow.Application.Commands.ReplayInboxMessage;

using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class ReplayInboxMessageCommandHandler
    : IRequestHandler<ReplayInboxMessageCommand, Result>
{
    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowIntegrationInboxRepository _repo;

    public ReplayInboxMessageCommandHandler(IWorkflowFeatureGate gate, IWorkflowIntegrationInboxRepository repo)
    {
        _gate = gate;
        _repo = repo;
    }

    public async Task<Result> Handle(ReplayInboxMessageCommand request, CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure) return gate;

        var message = await _repo.GetByIdAsync(request.MessageId, cancellationToken);
        if (message is null || message.OrganizationId != request.OrganizationId)
            return Result.Failure(new Error("Workflow.Inbox.NotFound", "Inbox message was not found."));

        if (message.Status is not (WorkflowInboxStatus.DeadLetter or WorkflowInboxStatus.Failed))
            return Result.Failure(new Error("Workflow.Inbox.NotDeadLetter", "Only dead-letter or failed inbox messages can be replayed."));

        message.ResetForReplay(DateTime.UtcNow);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
