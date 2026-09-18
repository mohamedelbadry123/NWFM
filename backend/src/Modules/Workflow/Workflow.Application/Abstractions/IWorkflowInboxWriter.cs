namespace Workflow.Application.Abstractions;

using Workflow.Domain.Entities;

/// <summary>
/// Writes inbound trigger messages to the workflow integration inbox for audit and crash recovery.
/// </summary>
public interface IWorkflowInboxWriter
{
    Task<WorkflowIntegrationInbox?> GetByMessageIdAsync(
        string messageId, CancellationToken cancellationToken = default);

    Task<WorkflowIntegrationInbox> WritePendingAsync(
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
        CancellationToken cancellationToken = default);
}
