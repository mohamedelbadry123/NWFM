namespace Workflow.Application.Abstractions;

using Workflow.Domain.Enums;

/// <summary>
/// Appends an immutable event to the workflow_events audit trail.
/// Wraps IWorkflowEventRepository.AppendAsync with a typed interface used by the engine.
/// </summary>
public interface IWorkflowEventAppender
{
    Task AppendAsync(
        Guid organizationId,
        Guid workflowInstanceId,
        WorkflowEventType eventType,
        DateTime occurredAt,
        string? activityNodeKey = null,
        Guid? actorUserId = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default);
}
