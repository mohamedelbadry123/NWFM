namespace Workflow.Application.Helpers;

using Workflow.Application.Abstractions;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Repositories;

public sealed class WorkflowHistoryBuilder : IWorkflowHistoryBuilder
{
    private readonly IWorkItemRepository _workItems;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowParticipantRepository _participants;

    public WorkflowHistoryBuilder(
        IWorkItemRepository workItems,
        IWorkflowVersionRepository versions,
        IWorkflowParticipantRepository participants)
    {
        _workItems = workItems;
        _versions = versions;
        _participants = participants;
    }

    public async Task<IReadOnlyList<WorkflowEventDto>> BuildAsync(
        WorkflowInstance instance,
        IReadOnlyList<WorkflowEvent> events,
        IReadOnlyList<ActivityInstance> activities,
        bool crossTenant,
        CancellationToken cancellationToken = default)
    {
        var workItems = crossTenant
            ? await _workItems.GetByInstanceIdForSuperAdminAsync(instance.Id, cancellationToken)
            : await _workItems.GetByInstanceIdAsync(instance.Id, cancellationToken);

        var version = await _versions.GetByIdWithProjectionAsync(
            instance.PinnedWorkflowVersionId, cancellationToken);

        var userIds = events
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var participants = await _participants.GetByUserIdsAsync(
            instance.OrganizationId, userIds, crossTenant, cancellationToken);

        var actors = participants
            .GroupBy(p => p.UserId)
            .ToDictionary(
                g => g.Key,
                g => new WorkflowActorName(g.Last().DisplayName, g.Last().DisplayNameAr));

        return WorkflowHistoryComposer.Compose(
            events,
            activities,
            version?.Activities ?? [],
            workItems,
            actors);
    }
}
