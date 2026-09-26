namespace Workflow.Application.Mapping;

using Workflow.Application.Abstractions;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.DTOs;
using Workflow.Application.Queries.GetWorkflowVersionById;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Application.Helpers;
using Workflow.Application.Integrations;
using System.Text.Json;

public sealed class WorkItemDtoAssembler : IWorkItemDtoAssembler
{
    private readonly IWorkflowRequestRepository _requests;
    private readonly IWorkflowAssignmentGroupRepository _groups;
    private readonly IWorkflowInstanceRepository _instances;
    private readonly IActivityInstanceRepository _activities;
    private readonly IWorkflowVersionRepository _versions;
    private readonly IWorkflowVariableRepository? _variables;

    public WorkItemDtoAssembler(
        IWorkflowRequestRepository requests,
        IWorkflowAssignmentGroupRepository groups,
        IWorkflowInstanceRepository instances,
        IActivityInstanceRepository activities,
        IWorkflowVersionRepository versions, IWorkflowVariableRepository? variables = null)
    {
        _requests = requests;
        _groups = groups;
        _instances = instances;
        _activities = activities;
        _versions = versions;
        _variables = variables;
    }

    public async Task<WorkItemDto> ToDtoAsync(
        WorkItem item, bool includeOutcomes, CancellationToken cancellationToken = default)
    {
        var list = await ToDtoListAsync([item], cancellationToken);
        var dto = list[0];
        if (!includeOutcomes)
            return dto;

        var outcomes = await LoadOutcomesAsync(item, cancellationToken);
        var instance = await _instances.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
        var activity = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
        var version = instance is null ? null : await _versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        var definition = version?.Activities.FirstOrDefault(a => a.NodeKey == activity?.ActivityNodeKey);
        try
        {
            var config = WorkflowTaskForm.Parse(definition?.ConfigurationJson);
            var values = new Dictionary<string, object?>();
            if (item.FormDataJson is not null) values = JsonSerializer.Deserialize<Dictionary<string, object?>>(item.FormDataJson) ?? [];
            else if (_variables is not null && config.InputMappingJson is not null)
            {
                var variables = (await _variables.GetByInstanceIdAsync(item.WorkflowInstanceId, cancellationToken))
                    .ToDictionary(v => v.VariableName, v => JsonSerializer.Deserialize<JsonElement>(v.ValueJson ?? "null"));
                values = IntegrationValueMapper.Map(JsonSerializer.Serialize(new { variables }), WorkflowTaskForm.Mappings(config.InputMappingJson));
            }
            return dto with { AvailableOutcomes = outcomes, FormFields = config.FormFields, FormValues = values,
                InstructionsEn = config.InstructionsEn, InstructionsAr = config.InstructionsAr };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return dto with { AvailableOutcomes = outcomes }; }
    }

    public async Task<IReadOnlyList<WorkItemDto>> ToDtoListAsync(
        IReadOnlyList<WorkItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            return [];

        var orgId = items[0].OrganizationId;
        var instanceIds = items.Select(i => i.WorkflowInstanceId).Distinct().ToList();
        var groupIds = items.Select(i => i.AssignmentGroupId).Distinct().ToList();

        var requests = await _requests.GetByInstanceIdsAsync(orgId, instanceIds, cancellationToken);
        var requestByInstance = requests.ToDictionary(r => r.WorkflowInstanceId);
        var names = await _groups.GetNamesByIdsAsync(orgId, groupIds, cancellationToken);

        var originalIds = requests
            .Where(r => r.OriginalAssignedGroupId is not null)
            .Select(r => r.OriginalAssignedGroupId!.Value)
            .Distinct()
            .ToList();
        var originalNames = originalIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _groups.GetNamesByIdsAsync(orgId, originalIds, cancellationToken);

        var now = DateTime.UtcNow;
        return items.Select(item =>
        {
            requestByInstance.TryGetValue(item.WorkflowInstanceId, out var request);
            names.TryGetValue(item.AssignmentGroupId, out var groupName);
            string? originalName = null;
            if (request?.OriginalAssignedGroupId is Guid og)
                originalNames.TryGetValue(og, out originalName);

            int? remaining = null;
            if (item.DueAt is DateTime due
                && item.Status is WorkItemStatus.Pending or WorkItemStatus.Claimed)
            {
                remaining = (int)Math.Round((due - now).TotalMinutes);
            }

            int? slaMinutes = request?.CurrentTaskSlaMinutes;
            if (slaMinutes is null && item.DueAt is DateTime dueAt)
                slaMinutes = Math.Max(0, (int)Math.Round((dueAt - item.CreatedAt).TotalMinutes));

            var baseDto = ClaimWorkItemCommandHandler.MapToDto(item);
            return baseDto with
            {
                AssignmentGroupName = groupName,
                RequestNumber = request?.RequestNumber,
                ServiceNameEn = request?.ServiceNameEn,
                ServiceNameAr = request?.ServiceNameAr,
                RequestDate = request?.RequestDate,
                CurrentStepNameEn = request?.CurrentActivityNameEn,
                CurrentStepNameAr = request?.CurrentActivityNameAr,
                SlaDurationMinutes = slaMinutes,
                RemainingSlaMinutes = remaining,
                OriginalGroupName = originalName,
            };
        }).ToList();
    }

    private async Task<IReadOnlyList<ActivityOutcomeDefinitionDto>?> LoadOutcomesAsync(
        WorkItem item, CancellationToken cancellationToken)
    {
        var instance = await _instances.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
        if (instance is null)
            return null;

        var activity = await _activities.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
        var version = await _versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, cancellationToken);
        if (version is null)
            return null;

        var def = version.Activities.FirstOrDefault(a =>
            a.NodeKey == (activity?.ActivityNodeKey ?? instance.CurrentActivityNodeKey));
        if (def is null || def.Outcomes.Count == 0)
            return [];

        return def.Outcomes
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .Select(GetWorkflowVersionByIdQueryHandler.MapOutcome)
            .ToList();
    }
}
