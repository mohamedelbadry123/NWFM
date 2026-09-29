using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowActivityEvents(WorkflowDbContext db, IWorkflowIntegrationRuntime integrations)
{
    public async Task<Result<bool>> QueueAsync(WorkflowInstance instance, ActivityDefinition definition, ActivityInstance execution,
        string trigger, string occurrence, CancellationToken ct, Dictionary<string, object?>? eventValues = null)
    {
        var configured = (IntegrationJson.Read<BusinessActivityConfiguration>(definition.ConfigurationJson).Events ?? []).Where(e => e is not null && e.Trigger == trigger).ToList();
        var nodes = await db.ActivityDefinitions.Where(a => a.WorkflowVersionId == definition.WorkflowVersionId
            && a.ConfigurationJson != null && a.ConfigurationJson.Contains("triggerBinding")).ToListAsync(ct);
        foreach (var node in nodes)
        {
            var binding = WorkspaceDesign.Binding(node.ConfigurationJson);
            if (binding?.SourceNodeKey != definition.NodeKey || binding.Trigger != trigger) continue;
            var config = WorkspaceDesign.Configuration(node.ConfigurationJson);
            var kind = node.ActivityType == Workflow.Domain.Enums.ActivityType.NotificationTask ? "Email" : config["protocol"]?.GetValue<string>() switch { "Soap" => "Soap", "Sms" => "Sms", _ => "Http" };
            config["eventNodeKey"] = node.NodeKey;
            configured.Add(new ActivityEventConfiguration { Id = node.NodeKey, Name = node.Name, Trigger = trigger, Kind = kind,
                Required = trigger is not ("OnFailure" or "OnSlaReminder" or "OnSlaBreach") && (config["required"]?.GetValue<bool>() ?? kind is "Http" or "Soap"),
                Configuration = JsonSerializer.SerializeToElement(config) });
        }
        if (configured.Count == 0) return Result.Success(true);
        var variables = (await db.WorkflowVariables.Where(v => v.WorkflowInstanceId == instance.Id).ToListAsync(ct))
            .ToDictionary(v => v.VariableName, v => (object?)JsonSerializer.Deserialize<JsonElement>(v.ValueJson ?? "null"));
        if (eventValues is not null) foreach (var (key, value) in eventValues) variables[key] = value;
        foreach (var (key, value) in LocationVariables(instance)) variables[key] = value;
        var ready = true;
        foreach (var item in configured)
        {
            var key = $"event:{execution.Id}:{trigger}:{occurrence}:{item.Id}";
            var job = await db.IntegrationJobs.FirstOrDefaultAsync(j => j.OperationKey == key, ct);
            if (job is null)
            {
                var result = await integrations.QueueAsync(instance.Id, execution.Id, item.Kind == "Email" ? "Email" : "Http", item.DeliveryConfiguration(), variables, key, ct);
                if (result.IsFailure) return Result.Failure<bool>(result.Error);
                job = await db.IntegrationJobs.SingleAsync(j => j.OperationKey == key, ct);
                // A late background failure cannot reopen a finished activity or alter its selected route.
                job.ConfigureEvent(item.Required && execution.Status == Workflow.Domain.Enums.ActivityInstanceStatus.Active, trigger, item.Name);
                if (item.Configuration.TryGetProperty("eventNodeKey", out var nodeKey)) job.SetEventNode(nodeKey.GetString()!);
            }
            if (job.Required && job.Status != "Completed") ready = false;
        }
        await db.SaveChangesAsync(ct);
        return Result.Success(ready);
    }

    /// <summary>
    /// The instance's organization location as event template variables. <c>RegionCode</c> and
    /// <c>CityCode</c> are the names templates used before the shared hierarchy; they always carried the
    /// CBU and branch codes, so they keep those values for event configurations already published.
    /// Remove them once no published event template references <c>{{RegionCode}}</c> or <c>{{CityCode}}</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> LocationVariables(WorkflowInstance instance)
    {
        var location = instance.Location.Normalized();
        if (location.IsEmpty) return new Dictionary<string, object?>();
        return new Dictionary<string, object?>
        {
            ["ClusterCode"] = location.ClusterCode, ["CbuCode"] = location.CbuCode,
            ["BranchCode"] = location.BranchCode, ["OperationAreaCode"] = location.OperationAreaCode,
            ["RegionCode"] = location.CbuCode, ["CityCode"] = location.BranchCode,
        };
    }
}
