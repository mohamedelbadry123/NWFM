namespace Workflow.Application.Helpers;

using System.Text.Json;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public readonly record struct WorkflowActorName(string NameEn, string? NameAr);

public static class WorkflowHistoryComposer
{
    public static IReadOnlyList<WorkflowEventDto> Compose(
        IReadOnlyList<WorkflowEvent> events,
        IReadOnlyList<ActivityInstance> activities,
        IReadOnlyList<ActivityDefinition> definitions,
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyDictionary<Guid, WorkflowActorName> actors)
    {
        var activitiesByKey = activities
            .GroupBy(a => a.ActivityNodeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var definitionsByKey = definitions
            .GroupBy(d => d.NodeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        return events
            .OrderBy(e => e.OccurredAt)
            .Select(e => Map(e, activities, activitiesByKey, definitionsByKey, workItems, actors))
            .ToList();
    }

    private static WorkflowEventDto Map(
        WorkflowEvent evt,
        IReadOnlyList<ActivityInstance> activities,
        IReadOnlyDictionary<string, ActivityInstance> activitiesByKey,
        IReadOnlyDictionary<string, ActivityDefinition> definitionsByKey,
        IReadOnlyList<WorkItem> workItems,
        IReadOnlyDictionary<Guid, WorkflowActorName> actors)
    {
        var payload = ParsePayload(evt.PayloadJson);
        var workItem = MatchWorkItem(evt, activities, workItems);

        var nodeKey = FirstNonEmpty(
            evt.ActivityNodeKey,
            workItem is null ? null : activities.FirstOrDefault(a => a.Id == workItem.ActivityInstanceId)?.ActivityNodeKey);

        activitiesByKey.TryGetValue(nodeKey ?? string.Empty, out var activity);
        definitionsByKey.TryGetValue(nodeKey ?? string.Empty, out var definition);

        var nameEn = ResolveStepName(definition?.Name, activity?.Name, nodeKey);
        var nameAr = FirstNonEmpty(NullIfGeneric(definition?.NameAr), nameEn);

        string? actorEn = null;
        string? actorAr = null;
        if (evt.ActorUserId is Guid actorId && actors.TryGetValue(actorId, out var actor))
        {
            actorEn = actor.NameEn;
            actorAr = actor.NameAr;
        }

        var comment = FirstNonEmpty(
            workItem?.CommentText,
            ReadString(payload, "comment", "commentText", "Comment"));
        var action = FirstNonEmpty(
            workItem?.ActionTaken,
            ReadString(payload, "outcome", "actionTaken", "ActionTaken"));
        var attachmentName = ReadString(payload, "attachmentName", "attachmentFileName", "fileName");
        var attachmentUrl = ReadString(payload, "attachmentUrl", "fileUrl");

        return new WorkflowEventDto(
            evt.Id,
            evt.WorkflowInstanceId,
            evt.EventType,
            nodeKey,
            evt.ActorUserId,
            evt.PayloadJson,
            evt.OccurredAt,
            nameEn,
            nameAr,
            actorEn,
            actorAr,
            comment,
            action,
            attachmentName,
            attachmentUrl);
    }

    private static WorkItem? MatchWorkItem(
        WorkflowEvent evt,
        IReadOnlyList<ActivityInstance> activities,
        IReadOnlyList<WorkItem> workItems)
    {
        var nodeIds = activities
            .Where(a => !string.IsNullOrWhiteSpace(evt.ActivityNodeKey)
                        && string.Equals(a.ActivityNodeKey, evt.ActivityNodeKey, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Id)
            .ToHashSet();

        IEnumerable<WorkItem> candidates = workItems;
        if (nodeIds.Count > 0)
            candidates = candidates.Where(w => nodeIds.Contains(w.ActivityInstanceId));

        if (evt.EventType == WorkflowEventType.WorkItemCompleted)
        {
            return candidates
                .Where(w => w.Status == WorkItemStatus.Completed)
                .Where(w => !evt.ActorUserId.HasValue || w.CompletedByUserId == evt.ActorUserId)
                .OrderBy(w => DeltaSeconds(w.CompletedAt ?? w.CreatedAt, evt.OccurredAt))
                .FirstOrDefault();
        }

        if (evt.EventType is WorkflowEventType.WorkItemClaimed or WorkflowEventType.WorkItemReleased)
        {
            return candidates
                .Where(w => !evt.ActorUserId.HasValue
                            || w.ClaimedByUserId == evt.ActorUserId
                            || w.CompletedByUserId == evt.ActorUserId)
                .OrderBy(w => DeltaSeconds(w.ClaimedAt ?? w.CreatedAt, evt.OccurredAt))
                .FirstOrDefault();
        }

        return candidates.LastOrDefault();
    }

    private static double DeltaSeconds(DateTime left, DateTime right)
        => Math.Abs((left - right).TotalSeconds);

    private static JsonElement? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? payload, params string[] names)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root) return null;
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        return null;
    }

    public static IReadOnlyList<ActivityInstanceDto> RelabelActivities(
        IReadOnlyList<ActivityInstance> activities,
        IReadOnlyList<WorkflowEventDto> events)
    {
        var namesByKey = events
            .Where(e => !string.IsNullOrWhiteSpace(e.ActivityNodeKey))
            .GroupBy(e => e.ActivityNodeKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ActivityNameEn).LastOrDefault(n => !IsGenericStepName(n))
                     ?? g.Last().ActivityNameEn,
                StringComparer.OrdinalIgnoreCase);

        return activities.Select(a =>
        {
            namesByKey.TryGetValue(a.ActivityNodeKey, out var fromEvents);
            var name = ResolveStepName(fromEvents, a.Name, a.ActivityNodeKey);
            return new ActivityInstanceDto(
                a.Id,
                a.WorkflowInstanceId,
                a.ActivityNodeKey,
                a.ActivityType,
                name,
                a.Status,
                a.StartedAt,
                a.CompletedAt,
                a.FailedAt,
                a.FailureReason);
        }).ToList();
    }

    public static string ResolveStepName(string? preferred, string? fallback, string? nodeKey)
        => FirstNonEmpty(NullIfGeneric(preferred), NullIfGeneric(fallback), preferred, fallback, nodeKey)
           ?? string.Empty;

    public static bool IsGenericStepName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        return trimmed.Equals("User Task", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("UserTask", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("New Task", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("مهمة مستخدم", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NullIfGeneric(string? name)
        => IsGenericStepName(name) ? null : name;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
