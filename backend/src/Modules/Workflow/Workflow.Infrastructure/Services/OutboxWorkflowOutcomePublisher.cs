namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using NWFM.Shared.Integration.Workflow;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

/// <summary>
/// Persists outcome messages to the workflow integration outbox for reliable delivery.
/// </summary>
internal sealed class OutboxWorkflowOutcomePublisher : IWorkflowOutcomePublisher
{
    private readonly IWorkflowIntegrationOutboxRepository _outbox;

    public OutboxWorkflowOutcomePublisher(IWorkflowIntegrationOutboxRepository outbox)
        => _outbox = outbox;

    public async Task PublishAsync(WorkflowOutcomeMessage message, CancellationToken cancellationToken = default)
    {
        var existing = await _outbox.GetByMessageIdAsync(message.MessageId, cancellationToken);
        if (existing is not null)
            return;

        var payloadJson = JsonSerializer.Serialize(new
        {
            message.ApprovedOutputs,
            message.OccurredAt,
            message.CorrelationId,
        });

        var row = WorkflowIntegrationOutbox.Create(
            organizationId: message.OrganizationId,
            messageId: message.MessageId,
            bindingId: message.BindingId,
            workflowInstanceId: message.WorkflowInstanceId,
            moduleKey: message.ModuleKey,
            businessEntityType: message.BusinessEntityType,
            businessEntityId: message.BusinessEntityId,
            outcomeKey: message.OutcomeKey,
            payloadJson: payloadJson,
            createdAt: message.OccurredAt,
            correlationId: message.CorrelationId);

        await _outbox.AddAsync(row, cancellationToken);
    }
}
