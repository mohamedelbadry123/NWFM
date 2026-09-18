namespace Workflow.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

/// <summary>
/// Writes outcomes to the integration outbox, then attempts immediate delivery.
/// <see cref="Background.WorkflowOutboxHostedService"/> retries any leftover Pending rows.
/// </summary>
internal sealed class WorkflowOutcomeDispatcher : IWorkflowOutcomeDispatcher
{
    private readonly IWorkflowOutcomePublisher _publisher;
    private readonly IWorkflowIntegrationOutboxRepository _outbox;
    private readonly IEnumerable<IWorkflowOutcomeHandler> _handlers;
    private readonly ILogger<WorkflowOutcomeDispatcher> _logger;

    public WorkflowOutcomeDispatcher(
        IWorkflowOutcomePublisher publisher,
        IWorkflowIntegrationOutboxRepository outbox,
        IEnumerable<IWorkflowOutcomeHandler> handlers,
        ILogger<WorkflowOutcomeDispatcher> logger)
    {
        _publisher = publisher;
        _outbox    = outbox;
        _handlers  = handlers;
        _logger    = logger;
    }

    public async Task<Result> DispatchAsync(
        WorkflowOutcomeMessage message,
        CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(message, cancellationToken);

        var row = await _outbox.GetByMessageIdAsync(message.MessageId, cancellationToken);
        if (row is null)
            return Result.Failure(new Error("Workflow.OutcomeOutboxMissing", "The workflow outcome was not persisted to the outbox."));

        if (row.Status != WorkflowOutboxStatus.Pending)
            return Result.Success();

        var isShadow = IsShadow(message);
        if (isShadow)
        {
            row.MarkPublished(DateTime.UtcNow);
            await _outbox.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Shadow outcome recorded (no module mutation). Instance={InstanceId} Outcome={OutcomeKey}",
                message.WorkflowInstanceId, message.OutcomeKey);
            return Result.Success();
        }

        try
        {
            var matchingHandlers = _handlers.Where(h =>
                    string.Equals(h.ModuleKey, message.ModuleKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingHandlers.Length == 0)
                return Result.Failure(new Error("Workflow.OutcomeHandlerMissing", $"No outcome handler is registered for module '{message.ModuleKey}'."));

            foreach (var handler in matchingHandlers)
            {
                await handler.HandleAsync(message, cancellationToken);
            }

            row.MarkPublished(DateTime.UtcNow);
            await _outbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Outcome delivered immediately. Instance={InstanceId} Module={ModuleKey} Outcome={OutcomeKey}",
                message.WorkflowInstanceId, message.ModuleKey, message.OutcomeKey);
        }
        catch (Exception ex)
        {
            // Leave Pending for WorkflowOutboxHostedService retry.
            _logger.LogWarning(ex,
                "Immediate outcome delivery failed; left Pending for outbox worker. MessageId={MessageId}",
                message.MessageId);
            return Result.Failure(new Error("Workflow.OutcomeDeliveryFailed", "The business outcome could not be applied and remains pending for recovery."));
        }

        return Result.Success();
    }

    private static bool IsShadow(WorkflowOutcomeMessage message)
    {
        if (message.ApprovedOutputs is null)
            return false;

        if (!message.ApprovedOutputs.TryGetValue("IsShadow", out var raw) || raw is null)
            return false;

        return raw switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => string.Equals(raw.ToString(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }
}
