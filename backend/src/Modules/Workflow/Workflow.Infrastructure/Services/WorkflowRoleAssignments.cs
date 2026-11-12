using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using Workflow.Application.Abstractions;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowRoleAssignments(WorkflowDbContext db, IWorkflowRoleDirectory directory,
    IWorkflowAssignmentGroupRepository groups) : IWorkflowRoleAssignments
{
    public async Task<bool> CanAccessAsync(WorkItem item, Guid userId, CancellationToken ct)
    {
        if (item.AssignmentGroupId != Guid.Empty)
            return await groups.IsUserMemberOfGroupAsync(item.AssignmentGroupId, userId, item.OrganizationId, ct);
        if (!await db.Participants.AnyAsync(p => p.UserId == userId && p.OrganizationId == item.OrganizationId && p.IsActive, ct)) return false;
        var roles = await directory.GetRoleIdsForUserAsync(userId, ct);
        if (!await db.WorkItemCandidates.AnyAsync(c => c.OrganizationId == item.OrganizationId && c.WorkItemId == item.Id
            && c.CandidateType == WorkItemCandidateType.Role && c.ReferenceId != null && roles.Contains(c.ReferenceId.Value), ct)) return false;
        var scope = await directory.GetScopeForUserAsync(userId, ct);
        return scope is not null && await CoversAsync(item, scope, ct);
    }

    public async Task<IReadOnlyList<WorkItem>> AvailableAsync(Guid organizationId, Guid userId, bool overdue, CancellationToken ct)
    {
        if (!await db.Participants.AnyAsync(p => p.OrganizationId == organizationId && p.UserId == userId && p.IsActive, ct)) return [];
        var roles = await directory.GetRoleIdsForUserAsync(userId, ct);
        var now = DateTime.UtcNow;
        var items = await db.WorkItems.Where(w => w.OrganizationId == organizationId && w.AssignmentGroupId == Guid.Empty
            && (w.Status == WorkItemStatus.Pending || overdue && w.Status == WorkItemStatus.Claimed && w.ClaimedByUserId == userId)
            && (!overdue || w.DueAt != null && w.DueAt < now)
            && db.WorkItemCandidates.Any(c => c.WorkItemId == w.Id && c.OrganizationId == organizationId && c.CandidateType == WorkItemCandidateType.Role
                && c.ReferenceId != null && roles.Contains(c.ReferenceId.Value)))
            .OrderBy(w => w.CreatedAt).ToListAsync(ct);
        if (items.Count == 0) return items;
        var scope = await directory.GetScopeForUserAsync(userId, ct);
        if (scope is null) return [];
        if (scope.IsUnrestricted) return items;
        var covered = new List<WorkItem>();
        foreach (var item in items)
            if (await CoversAsync(item, scope, ct)) covered.Add(item);
        return covered;
    }

    private async Task<bool> CoversAsync(WorkItem item, OrgScopeSet scope, CancellationToken ct)
    {
        if (scope.IsUnrestricted) return true;
        var instance = await db.WorkflowInstances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == item.WorkflowInstanceId && i.OrganizationId == item.OrganizationId, ct);
        if (instance is null) return false;
        var execution = await db.ActivityInstances.AsNoTracking().SingleOrDefaultAsync(a => a.Id == item.ActivityInstanceId && a.WorkflowInstanceId == instance.Id, ct);
        if (execution is null) return false;
        var configJson = await db.ActivityDefinitions.AsNoTracking().Where(a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId && a.NodeKey == execution.ActivityNodeKey)
            .Select(a => a.ConfigurationJson).SingleOrDefaultAsync(ct);
        if (configJson is null) return false;
        var config = IntegrationJson.Read<BusinessActivityConfiguration>(configJson);
        return WorkflowScopeFilter.Allows(instance, scope, config.DepartmentCode, config.FieldActivityCode);
    }

    public async Task<IReadOnlyList<WorkItemCandidate>> CreateCandidatesAsync(Guid organizationId, Guid workItemId,
        IReadOnlyList<Guid> roleIds, DateTime now, CancellationToken ct)
    {
        var roles = (await directory.ListAsync(ct)).Where(r => roleIds.Contains(r.Id)).ToList();
        if (roleIds.Count == 0 || roleIds.Count > 50 || roleIds.Distinct().Count() != roleIds.Count || roles.Count != roleIds.Count)
            throw new InvalidOperationException("Select one or more existing account roles (maximum 50).");
        var users = await directory.GetUserIdsForRolesAsync(roleIds.ToArray(), ct);
        var participants = await db.Participants.Where(p => p.OrganizationId == organizationId && p.IsActive && users.Contains(p.UserId)).ToListAsync(ct);
        return roles.Select(role => WorkItemCandidate.Create(organizationId, workItemId, WorkItemCandidateType.Role, now, referenceId: role.Id, displayName: role.NameEn))
            .Concat(participants.DistinctBy(p => p.UserId).Select(p => WorkItemCandidate.Create(organizationId, workItemId, WorkItemCandidateType.User,
                now, referenceId: p.UserId, displayName: p.DisplayName))).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct)
    {
        if (workItemIds.Count == 0) return new Dictionary<Guid, string>();
        var rows = await db.WorkItemCandidates.AsNoTracking().Where(c => workItemIds.Contains(c.WorkItemId) && c.CandidateType == WorkItemCandidateType.Role)
            .Select(c => new { c.WorkItemId, c.DisplayName }).ToListAsync(ct);
        return rows.GroupBy(c => c.WorkItemId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(c => c.DisplayName).Where(n => n != null).Distinct().Order()));
    }
}
