namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;
using Workflow.Domain.Enums;

/// <summary>
/// Snapshot of who can claim a WorkItem when a UserTask is created.
/// </summary>
public sealed class WorkItemCandidate : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid WorkItemId { get; private set; }
    public WorkItemCandidateType CandidateType { get; private set; }
    public Guid? ReferenceId { get; private set; }

    /// <summary>Role key or expression when ReferenceId is not used.</summary>
    public string? ReferenceKey { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsPrimary { get; private set; }

    private WorkItemCandidate() { }

    public static WorkItemCandidate Create(
        Guid organizationId,
        Guid workItemId,
        WorkItemCandidateType candidateType,
        DateTime createdAt,
        Guid? referenceId = null,
        string? referenceKey = null,
        string? displayName = null,
        bool isPrimary = false)
    {
        return new WorkItemCandidate
        {
            Id             = Guid.NewGuid(),
            OrganizationId = organizationId,
            WorkItemId     = workItemId,
            CandidateType  = candidateType,
            ReferenceId    = referenceId,
            ReferenceKey   = referenceKey,
            DisplayName    = displayName,
            IsPrimary      = isPrimary,
            CreatedAt      = createdAt,
        };
    }
}
