namespace Workflow.Infrastructure.Services;

using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowEventAppender : IWorkflowEventAppender
{
    private readonly IWorkflowEventRepository _repo;

    public WorkflowEventAppender(IWorkflowEventRepository repo) => _repo = repo;

    public Task AppendAsync(
        Guid organizationId,
        Guid workflowInstanceId,
        WorkflowEventType eventType,
        DateTime occurredAt,
        string? activityNodeKey = null,
        Guid? actorUserId = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        var evt = WorkflowEvent.Append(
            organizationId, workflowInstanceId, eventType, occurredAt,
            activityNodeKey, actorUserId, payloadJson);
        return _repo.AppendAsync(evt, cancellationToken);
    }
}
