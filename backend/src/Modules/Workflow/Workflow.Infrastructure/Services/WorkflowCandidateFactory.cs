namespace Workflow.Infrastructure.Services;

using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowCandidateFactory : IWorkflowCandidateFactory
{
    private readonly IWorkflowAssignmentGroupRepository _groupRepo;
    private readonly IWorkItemRepository _workItemRepo;

    public WorkflowCandidateFactory(
        IWorkflowAssignmentGroupRepository groupRepo,
        IWorkItemRepository workItemRepo)
    {
        _groupRepo     = groupRepo;
        _workItemRepo  = workItemRepo;
    }

    public async Task<IReadOnlyList<WorkItemCandidate>> CreateCandidatesAsync(
        Guid organizationId,
        Guid workItemId,
        Guid assignmentGroupId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        var group = await _groupRepo.GetByIdWithMembersAsync(
            assignmentGroupId, organizationId, cancellationToken);

        var candidates = new List<WorkItemCandidate>
        {
            WorkItemCandidate.Create(
                organizationId,
                workItemId,
                WorkItemCandidateType.Group,
                createdAt,
                referenceId: assignmentGroupId,
                displayName: group?.Name,
                isPrimary: group is null
                           || group.AssignmentStrategy == AssignmentStrategy.Manual),
        };

        if (group is null)
            return candidates;

        var now = createdAt;
        var claimable = group.Members
            .Where(m => m.Participant is { IsActive: true }
                        && (m.ValidFrom is null || m.ValidFrom <= now)
                        && (m.ValidTo is null || m.ValidTo >= now))
            .Select(m => m.Participant)
            .Where(p => p is not null)
            .DistinctBy(p => p!.UserId)
            .ToList();

        Guid? suggestedUserId = null;
        if (group.AssignmentStrategy != AssignmentStrategy.Manual && claimable.Count > 0)
        {
            suggestedUserId = await PickSuggestedUserAsync(
                organizationId, group.AssignmentStrategy, claimable!, cancellationToken);
        }

        foreach (var participant in claimable!)
        {
            var isPrimary = suggestedUserId.HasValue
                            && participant.UserId == suggestedUserId.Value;

            candidates.Add(WorkItemCandidate.Create(
                organizationId,
                workItemId,
                WorkItemCandidateType.User,
                createdAt,
                referenceId: participant.UserId,
                displayName: participant.DisplayName,
                isPrimary: isPrimary));
        }

        // When strategy picked a user, ensure the Group candidate is not primary.
        if (suggestedUserId.HasValue)
        {
            // Recreate list with corrected Group IsPrimary=false (entities are immutable after create).
            var rebuilt = new List<WorkItemCandidate>
            {
                WorkItemCandidate.Create(
                    organizationId,
                    workItemId,
                    WorkItemCandidateType.Group,
                    createdAt,
                    referenceId: assignmentGroupId,
                    displayName: group.Name,
                    isPrimary: false),
            };
            rebuilt.AddRange(candidates.Where(c => c.CandidateType == WorkItemCandidateType.User));
            return rebuilt;
        }

        return candidates;
    }

    private async Task<Guid?> PickSuggestedUserAsync(
        Guid organizationId,
        AssignmentStrategy strategy,
        IReadOnlyList<WorkflowParticipant> claimable,
        CancellationToken cancellationToken)
    {
        return strategy switch
        {
            AssignmentStrategy.Random =>
                claimable[Random.Shared.Next(claimable.Count)].UserId,

            AssignmentStrategy.FirstAvailable =>
                claimable
                    .OrderBy(p => p.DisplayName)
                    .Select(p => (Guid?)p.UserId)
                    .FirstOrDefault(),

            AssignmentStrategy.LeastBusy => await PickLeastBusyAsync(
                organizationId, claimable, cancellationToken),

            AssignmentStrategy.RoundRobin => await PickRoundRobinAsync(
                organizationId, claimable, cancellationToken),

            _ => null,
        };
    }

    private async Task<Guid?> PickLeastBusyAsync(
        Guid organizationId,
        IReadOnlyList<WorkflowParticipant> claimable,
        CancellationToken cancellationToken)
    {
        Guid? best = null;
        var bestCount = int.MaxValue;

        foreach (var p in claimable)
        {
            var count = await _workItemRepo.CountOpenClaimedByUserAsync(
                organizationId, p.UserId, cancellationToken);
            if (count < bestCount)
            {
                bestCount = count;
                best = p.UserId;
            }
        }

        return best;
    }

    private async Task<Guid?> PickRoundRobinAsync(
        Guid organizationId,
        IReadOnlyList<WorkflowParticipant> claimable,
        CancellationToken cancellationToken)
    {
        Guid? best = null;
        DateTime? oldestClaim = null;

        foreach (var p in claimable)
        {
            var last = await _workItemRepo.GetLastClaimedAtByUserAsync(
                organizationId, p.UserId, cancellationToken);

            // Prefer never-assigned, then least recently assigned.
            if (last is null)
                return p.UserId;

            if (oldestClaim is null || last < oldestClaim)
            {
                oldestClaim = last;
                best = p.UserId;
            }
        }

        return best;
    }
}
