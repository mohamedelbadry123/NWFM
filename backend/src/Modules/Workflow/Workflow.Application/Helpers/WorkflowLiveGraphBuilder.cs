namespace Workflow.Application.Helpers;

using System.Text.Json;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public static class WorkflowLiveGraphBuilder
{
    public static WorkflowLiveGraphDto Build(
        WorkflowInstance instance,
        WorkflowVersion version,
        IReadOnlyList<ActivityInstance> activityInstances)
        => Build(instance, version.Activities, version.Transitions, version.DesignerJson, activityInstances);

    public static WorkflowLiveGraphDto Build(
        WorkflowInstance instance,
        IReadOnlyList<ActivityDefinition> activities,
        IReadOnlyList<WorkflowTransition> transitions,
        string? designerJson,
        IReadOnlyList<ActivityInstance> activityInstances)
    {
        var positions = ReadDesignerPositions(designerJson);
        var statusByKey = ResolveStatuses(instance, activities, activityInstances);

        var nodes = new List<WorkflowLiveGraphNodeDto>(activities.Count);
        var i = 0;
        foreach (var activity in activities)
        {
            double x;
            double y;
            if (positions.TryGetValue(activity.NodeKey, out var pos))
            {
                x = pos.X;
                y = pos.Y;
            }
            else if (activity.PositionX is not null && activity.PositionY is not null)
            {
                x = activity.PositionX.Value;
                y = activity.PositionY.Value;
            }
            else
            {
                x = 120 + (i % 3) * 280;
                y = 100 + (i / 3) * 220;
            }

            var status = statusByKey.TryGetValue(activity.NodeKey, out var s) ? s : "Pending";
            nodes.Add(new WorkflowLiveGraphNodeDto(
                activity.NodeKey,
                activity.ActivityType.ToString(),
                activity.Name,
                x,
                y,
                status));
            i++;
        }

        var byId = activities.ToDictionary(a => a.Id);
        var edges = transitions
            .Where(t => byId.ContainsKey(t.FromActivityDefinitionId) && byId.ContainsKey(t.ToActivityDefinitionId))
            .Select(t => new WorkflowLiveGraphEdgeDto(
                byId[t.FromActivityDefinitionId].NodeKey,
                byId[t.ToActivityDefinitionId].NodeKey,
                t.IsDefault))
            .ToList();

        return new WorkflowLiveGraphDto(
            instance.CurrentActivityNodeKey,
            instance.Status.ToString(),
            nodes,
            edges);
    }

    private static Dictionary<string, string> ResolveStatuses(
        WorkflowInstance instance,
        IReadOnlyList<ActivityDefinition> activities,
        IReadOnlyList<ActivityInstance> activityInstances)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in activityInstances.OrderBy(a => a.StartedAt))
        {
            map[row.ActivityNodeKey] = row.Status switch
            {
                ActivityInstanceStatus.Completed => "Completed",
                ActivityInstanceStatus.Failed => "Failed",
                ActivityInstanceStatus.Skipped => "Completed",
                ActivityInstanceStatus.Active => "Active",
                _ => "Pending",
            };
        }

        var currentKey = instance.CurrentActivityNodeKey;

        // Pass-through nodes (ExclusiveGateway, Start) often remain Active in the store after
        // the token has already moved. Live graph may show only one Active node: the token.
        if (instance.Status == WorkflowInstanceStatus.Running
            && !string.IsNullOrWhiteSpace(currentKey))
        {
            foreach (var key in map.Keys.ToList())
            {
                if (!string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase)
                    && map[key] == "Active")
                {
                    map[key] = "Completed";
                }
            }

            if (!map.TryGetValue(currentKey, out var currentStatus) || currentStatus != "Failed")
                map[currentKey] = "Active";
        }

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
        {
            foreach (var key in map.Keys.ToList())
            {
                if (map[key] == "Active") map[key] = "Completed";
            }

            if (!string.IsNullOrWhiteSpace(currentKey))
                map[currentKey] = "Completed";
        }

        if (instance.Status != WorkflowInstanceStatus.Pending
            && !string.IsNullOrWhiteSpace(currentKey))
        {
            foreach (var activity in activities)
            {
                if (activity.ActivityType != ActivityType.Start) continue;
                if (string.Equals(activity.NodeKey, currentKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!map.TryGetValue(activity.NodeKey, out var startStatus) || startStatus == "Pending")
                    map[activity.NodeKey] = "Completed";
            }
        }

        return map;
    }

    private static Dictionary<string, (double X, double Y)> ReadDesignerPositions(string? designerJson)
    {
        var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(designerJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(designerJson);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var node in nodes.EnumerateArray())
            {
                var key = node.TryGetProperty("nodeKey", out var k) ? k.GetString() : null;
                if (string.IsNullOrWhiteSpace(key)) continue;
                var x = node.TryGetProperty("x", out var xv) ? xv.GetDouble() : 0;
                var y = node.TryGetProperty("y", out var yv) ? yv.GetDouble() : 0;
                result[key] = (x, y);
            }
        }
        catch (JsonException)
        {
            return result;
        }

        return result;
    }
}
