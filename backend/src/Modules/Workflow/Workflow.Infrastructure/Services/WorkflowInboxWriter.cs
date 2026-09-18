namespace Workflow.Infrastructure.Services;

using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

internal sealed class WorkflowInboxWriter : IWorkflowInboxWriter
{
    private readonly IWorkflowIntegrationInboxRepository _inbox;

    public WorkflowInboxWriter(IWorkflowIntegrationInboxRepository inbox) => _inbox = inbox;

    public Task<WorkflowIntegrationInbox?> GetByMessageIdAsync(
        string messageId, CancellationToken cancellationToken = default)
        => _inbox.GetByMessageIdAsync(messageId, cancellationToken);

    public async Task<WorkflowIntegrationInbox> WritePendingAsync(
        Guid organizationId,
        string messageId,
        string idempotencyKey,
        string moduleKey,
        string businessEntityType,
        string businessEntityId,
        string triggerEvent,
        string payloadJson,
        DateTime createdAt,
        string? correlationId = null,
        Guid? bindingId = null,
        CancellationToken cancellationToken = default)
    {
        var row = WorkflowIntegrationInbox.Create(
            organizationId,
            messageId,
            idempotencyKey,
            moduleKey,
            businessEntityType,
            businessEntityId,
            triggerEvent,
            payloadJson,
            createdAt,
            correlationId,
            bindingId);

        await _inbox.AddAsync(row, cancellationToken);
        return row;
    }
}
