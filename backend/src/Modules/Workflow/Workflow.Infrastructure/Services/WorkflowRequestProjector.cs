namespace Workflow.Infrastructure.Services;

using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

internal sealed class WorkflowRequestProjector : IWorkflowRequestProjector
{
    private readonly IWorkflowRequestRepository _requests;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IWorkflowBindingRepository _bindings;
    private readonly IWorkItemRepository _workItems;
    private readonly IActivityInstanceRepository _activities;

    public WorkflowRequestProjector(
        IWorkflowRequestRepository requests,
        IWorkflowInstanceRepository instances,
        IWorkflowBindingRepository bindings,
        IWorkItemRepository workItems,
        IActivityInstanceRepository activities)
    {
        _requests = requests;
        _instances = instances;
        _bindings = bindings;
        _workItems = workItems;
        _activities = activities;
    }

    public async Task EnsureCreatedAsync(
        WorkflowInstance instance, WorkflowBinding binding, DateTime now, CancellationToken cancellationToken = default)
    {
        var existing = await _requests.GetByInstanceIdAsync(instance.Id, instance.OrganizationId, cancellationToken);
        if (existing is not null)
            return;

        var (nameEn, nameAr) = ResolveServiceName(binding);
        var year = now.Year;
        var seq = await _requests.CountByOrgAndYearAsync(instance.OrganizationId, year, cancellationToken) + 1;
        var number = $"WF-{year}-{seq:D6}";

        var request = WorkflowRequest.Create(
            instance.OrganizationId,
            number,
            instance.WorkflowBindingId,
            instance.Id,
            binding.EntityType,
            instance.BusinessEntityId,
            binding.ModuleKey,
            nameEn,
            instance.StartedAt,
            instance.Status,
            binding.TriggerEvent,
            now,
            nameAr,
            binding.ScreenKey,
            instance.StartedByUserId,
            instance.CorrelationId);

        await _requests.AddAsync(request, cancellationToken);
        await _requests.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncCurrentTaskAsync(
        WorkflowInstance instance,
        WorkItem? workItem,
        string? activityNameEn,
        string? activityNameAr,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByInstanceIdAsync(instance.Id, instance.OrganizationId, cancellationToken);
        if (request is null)
            return;

        int? slaMinutes = null;
        if (workItem?.DueAt is DateTime due)
        {
            var minutes = (int)Math.Round((due - workItem.CreatedAt).TotalMinutes);
            slaMinutes = Math.Max(0, minutes);
        }

        request.SyncCurrentTask(
            workItem?.ActivityInstanceId,
            activityNameEn,
            activityNameAr,
            workItem?.AssignmentGroupId,
            slaMinutes,
            workItem?.DueAt,
            workItem?.ClaimedByUserId,
            now);

        request.SyncStatus(instance.Status, instance.CompletedAt ?? instance.CancelledAt, now);
        await _requests.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncStatusAsync(
        WorkflowInstance instance, DateTime now, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByInstanceIdAsync(instance.Id, instance.OrganizationId, cancellationToken);
        if (request is null)
            return;

        request.SyncStatus(instance.Status, instance.CompletedAt ?? instance.CancelledAt, now);
        await _requests.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncClaimAsync(
        Guid workflowInstanceId, Guid organizationId, Guid? claimedByUserId, DateTime now, CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByInstanceIdAsync(workflowInstanceId, organizationId, cancellationToken);
        if (request is null)
            return;

        request.SyncClaim(claimedByUserId, now);
        await _requests.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncAssignmentAsync(
        Guid workflowInstanceId,
        Guid organizationId,
        Guid assignmentGroupId,
        Guid? claimedByUserId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByInstanceIdAsync(workflowInstanceId, organizationId, cancellationToken);
        if (request is null)
            return;

        request.SyncAssignment(assignmentGroupId, claimedByUserId, now);
        await _requests.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillMissingForOrgAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var missingIds = await _requests.GetInstanceIdsMissingProjectionAsync(organizationId, 200, cancellationToken);
        foreach (var instanceId in missingIds)
        {
            var instance = await _instances.GetByIdAsync(instanceId, cancellationToken);
            if (instance is null || instance.OrganizationId != organizationId)
                continue;

            var binding = await _bindings.GetByIdAsync(instance.WorkflowBindingId, cancellationToken);
            if (binding is null)
                continue;

            await EnsureCreatedAsync(instance, binding, instance.StartedAt, cancellationToken);

            var workItems = await _workItems.GetByInstanceIdAsync(instance.Id, cancellationToken);
            var current = workItems.LastOrDefault(w =>
                w.Status is WorkItemStatus.Pending or WorkItemStatus.Claimed)
                ?? workItems.LastOrDefault();

            string? nameEn = null;
            if (current is not null)
            {
                var activity = await _activities.GetByIdAsync(current.ActivityInstanceId, cancellationToken);
                nameEn = activity?.Name;
            }

            await SyncCurrentTaskAsync(instance, current, nameEn, null, DateTime.UtcNow, cancellationToken);
        }
    }

    private static (string NameEn, string? NameAr) ResolveServiceName(WorkflowBinding binding)
    {
        var module = ModuleCatalog.GetAll()
            .FirstOrDefault(m => string.Equals(m.ModuleKey, binding.ModuleKey, StringComparison.OrdinalIgnoreCase));
        if (module is null)
            return (binding.ModuleKey, null);

        var entity = module.EntityTypes.FirstOrDefault(e =>
            string.Equals(e.EntityType, binding.EntityType, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            return (module.NameEn, module.NameAr);

        return (entity.NameEn, entity.NameAr);
    }
}
