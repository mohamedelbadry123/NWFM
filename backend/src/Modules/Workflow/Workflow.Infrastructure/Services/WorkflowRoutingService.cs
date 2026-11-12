using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Integrations;
using Workflow.Application.Routing;
using Workflow.Application.Settings;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowRoutingService(WorkflowDbContext db, IWorkflowVersionRepository versions,
    IFormGateway forms, IOptions<WorkflowSettings> settings, ISender sender) : IWorkflowRouting
{
    private async Task<WorkflowVersion?> VersionAsync(Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var version = await versions.GetByIdWithProjectionAsync(versionId, ct);
        return version?.WorkflowDefinitionId == definitionId && await db.WorkflowDefinitions.AnyAsync(d => d.Id == definitionId, ct) ? version : null;
    }
    private static Error Missing => new("Workflow.Version.NotFound", "Workflow version not found.");

    public async Task<Result<RoutingContext>> ContextAsync(Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var version = await VersionAsync(definitionId, versionId, ct);
        if (version is null) return Result.Failure<RoutingContext>(Missing);
        var graph = RoutingDefinition.Graph(version);
        return Result.Success(new RoutingContext(await SourcesAsync(version, ct), graph.Nodes, graph.Connections,
            settings.Value.RoutingV4Enabled, RoutingDraftRevision.Of(version)));
    }

    private async Task<IReadOnlyList<RoutingSource>> SourcesAsync(WorkflowVersion version, CancellationToken ct)
    {
        var sources = new List<RoutingSource>();
        foreach (var variable in version.Variables)
            sources.Add(new("$input." + variable.VariableKey, "$input", variable.VariableKey, variable.DataType switch
            { VariableDataType.Boolean => "yes_no", VariableDataType.Integer or VariableDataType.Decimal => "numeric", VariableDataType.Date => "date", VariableDataType.DateTime => "date_time", _ => "text" }, variable.Name, Sensitive: variable.IsSensitive));
        foreach (var node in version.Activities)
        {
            if (RoutingGraphRules.IsGate(node.ActivityType.ToString()) && node.ActivityType != ActivityType.JoinGateway || node.ActivityType is ActivityType.Start or ActivityType.End) continue;
            var config = WorkspaceDesign.Configuration(node.ConfigurationJson);
            if (config["required"]?.GetValue<bool>() == false) continue;
            sources.Add(new(node.NodeKey + ".$outcome", node.NodeKey, "$outcome", "text", node.Name + " → Outcome"));
            if (node.ActivityType == ActivityType.JoinGateway && RoutingDefinition.Read(node.ConfigurationJson)?.WaitFor == "Any")
                sources.Add(new(node.NodeKey + ".$winnerNode", node.NodeKey, "$winnerNode", "text", node.Name + " → Winning result source"));
            var business = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            foreach (var form in business.FormSteps)
                foreach (var field in await forms.GetFieldsAsync(form.FormId, form.VersionNo, ct))
                    sources.Add(new(node.NodeKey + "." + field.DataName, node.NodeKey, field.DataName, field.FieldType,
                        node.Name + " → " + (field.LabelEn ?? field.DataName), form.FormId, form.VersionNo, Choices: field.Choices));
            foreach (var output in config["routingOutputs"]?.Deserialize<List<RoutingOutput>>(IntegrationJson.Options) ?? [])
                sources.Add(new(node.NodeKey + "." + output.Key, node.NodeKey, output.Key, output.Type, node.Name + " → " + output.Label,
                    Sensitive: output.Sensitive, Choices: output.Choices));
        }
        return sources;
    }

    public async Task<IReadOnlyList<RoutingDiagnostic>> ValidateAsync(WorkflowVersion version, CancellationToken ct)
    {
        if (!RoutingDefinition.Enabled(version.WorkspaceJson)) return [];
        var errors = new List<RoutingDiagnostic>();
        if (!settings.Value.RoutingV4Enabled && version.IsDraft) errors.Add(new("ROUTING_DISABLED", "Routing version 4 authoring is not enabled in this environment."));
        RoutingGraph graph;
        try { graph = RoutingDefinition.Graph(version); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { return [new("ROUTING_CONFIG", "Routing configuration has an invalid structure or value.")]; }
        errors.AddRange(RoutingGraphRules.Validate(graph));
        foreach (var activity in version.Activities)
            if (WorkspaceDesign.Configuration(activity.ConfigurationJson)["authoring"]?["repairs"] is System.Text.Json.Nodes.JsonArray repairs)
                foreach (var repair in repairs.OfType<System.Text.Json.Nodes.JsonObject>())
                    errors.Add(new("ROUTING_REPAIR", repair["message"]?.GetValue<string>() ?? "Complete the preserved rule.", activity.NodeKey, repair["transitionKey"]?.GetValue<string>()));

        if (version.IsDraft)
            foreach (var node in graph.Nodes.Where(n => n.Routing is not null))
                if (RoutingEvaluator.Validate(node.Routing!).Count == 0)
                    foreach (var route in RoutingRuleConflicts.Find(node.Routing!))
                        errors.Add(new("ROUTING_OVERLAP", "These rules in " + node.Name + " can both match. Adjust them or choose priority.", node.Key, "matchMode"));
        if (errors.Count > 0) return errors;
        var available = await SourcesAsync(version, ct);
        foreach (var node in version.Activities)
        {
            var routing = RoutingDefinition.Read(node.ConfigurationJson);
            foreach (var source in routing?.Sources ?? [])
            {
                var actual = available.FirstOrDefault(s => s.Key == source.Key);
                if (actual is null || actual.NodeKey != source.NodeKey || actual.Field != source.Field || actual.Type != source.Type
                    || actual.FormId != source.FormId || actual.FormVersion != source.FormVersion
                    || JsonSerializer.Serialize(actual.Choices ?? []) != JsonSerializer.Serialize(source.Choices ?? []))
                    errors.Add(new("ROUTING_SOURCE_CHANGED", "Review the source field and pinned schema: " + source.Label, node.NodeKey, source.Key));
                if (source.NodeKey != "$input" && !HasPath(graph, source.NodeKey, node.NodeKey))
                    errors.Add(new("ROUTING_SOURCE_UNAVAILABLE", "This source cannot complete before the condition: " + source.Label, node.NodeKey, source.Key));
            }
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            if (config.FormSteps.Any(f => f.Rules.Count > 0 || f.Otherwise.Kind != "Next") || config.RejectTargetNodeKey is not null)
                errors.Add(new("ROUTING_LEGACY_RULES", "Move form and rejection routes into visible Condition components.", node.NodeKey));
            var json = WorkspaceDesign.Configuration(node.ConfigurationJson);
            var outputs = json["routingOutputs"]?.Deserialize<List<RoutingOutput>>(IntegrationJson.Options) ?? [];
            if (outputs.Count > 200 || outputs.Any(o => !System.Text.RegularExpressions.Regex.IsMatch(o.Key ?? "", "^[A-Za-z][A-Za-z0-9_]{0,99}$") || o.Type is not ("text" or "numeric" or "yes_no" or "date" or "date_time" or "single_choice" or "multiple_choice")) || outputs.Select(o => o.Key).Distinct().Count() != outputs.Count)
                errors.Add(new("ROUTING_OUTPUTS", "Declare unique output fields (at most 200).", node.NodeKey));
        }
        if (version.Transitions.Any(t => !string.IsNullOrWhiteSpace(t.ConditionExpression))) errors.Add(new("ROUTING_LEGACY_EXPRESSION", "Arrow expressions must be converted to typed conditions."));
        return errors;
    }

    private static bool HasPath(RoutingGraph graph, string from, string to)
    {
        var seen = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(from);
        while (pending.TryPop(out var key))
        { if (!seen.Add(key)) continue; foreach (var e in graph.Connections.Where(e => e.From == key)) { if (e.To == to) return true; pending.Push(e.To); } }
        return false;
    }

    public async Task<Result<RoutingSimulationResult>> SimulateAsync(Guid definitionId, Guid versionId, RoutingSimulationInput input, CancellationToken ct)
    {
        var version = await VersionAsync(definitionId, versionId, ct);
        if (version is null) return Result.Failure<RoutingSimulationResult>(Missing);
        if (input.Signals is null || input.Signals.Count > 500 || input.Signals.Any(s => s is null || s.AdvanceSeconds < 0 || s.AdvanceSeconds > 31536000 || s.NodeKey.Length > 200 || s.Outcome.Length > 100))
            return Result.Failure<RoutingSimulationResult>(new("Routing.SimulationInput", "Provide up to 500 bounded simulation inputs."));
        if (!RoutingDefinition.Enabled(version.WorkspaceJson)) return Result.Failure<RoutingSimulationResult>(new("Routing.Version", "Upgrade this draft before using routing simulation."));
        var diagnostics = await ValidateAsync(version, ct);
        return Result.Success(diagnostics.Count > 0 ? new RoutingSimulationResult([], [], diagnostics) : RoutingSimulator.Run(RoutingDefinition.Graph(version), input));
    }

    public async Task<Result<RoutingUpgradePreview>> PreviewUpgradeAsync(Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var version = await VersionAsync(definitionId, versionId, ct);
        if (version is null) return Result.Failure<RoutingUpgradePreview>(Missing);
        var conversion = RoutingUpgrade.Convert(version, await SourcesAsync(version, ct));
        var errors = conversion.Diagnostics.ToList();
        if (!settings.Value.RoutingV4Enabled) errors.Add(new("ROUTING_DISABLED", "Routing version 4 authoring is not enabled in this environment."));
        if (!version.IsDraft) errors.Add(new("ROUTING_DRAFT", "Clone this published version to a draft before upgrading."));
        return Result.Success(new RoutingUpgradePreview(errors.Count == 0, errors, conversion.ConditionsAdded, RoutingDraftRevision.Of(version)));
    }

    public Task<Result<string>> PrepareAsync(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct) =>
        WorkflowExecutionLock.RunAsync(db, "version:" + versionId, async () =>
        {
            var version = await VersionAsync(definitionId, versionId, ct);
            if (version is null) return Result.Failure<string>(Missing);
            if (!version.IsDraft || !settings.Value.RoutingV4Enabled) return Result.Failure<string>(new("Routing.Unavailable", "This draft cannot be prepared in this environment."));
            if (RoutingDefinition.Enabled(version.WorkspaceJson)) return Result.Success(RoutingDraftRevision.Of(version));
            if (RoutingDraftRevision.Of(version) != input.ExpectedRevision) return Result.Failure<string>(new("Workflow.Draft.Conflict", "A newer draft exists. Reload before editing."));
            var conversion = RoutingUpgrade.Convert(version, await SourcesAsync(version, ct), preserveRepairs: true);
            if (string.IsNullOrEmpty(conversion.Xml)) return Result.Failure<string>(new("Routing.Unavailable", "This workflow uses an older designer."));
            var snapshot = db.RoutingUpgradeSnapshots.Add(new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = await db.WorkflowDefinitions.Where(d => d.Id == definitionId).Select(d => d.OrganizationId).SingleAsync(ct),
                WorkflowVersionId = version.Id,
                Xml = version.XmlContent,
                DesignerJson = version.DesignerJson,
                WorkspaceJson = version.WorkspaceJson,
                CreatedAt = DateTime.UtcNow
            });
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(version.Id, conversion.Xml, RoutingUpgrade.PrepareDesignerJson(version, conversion.Xml), input.ExpectedRevision, AllowRoutingUpgrade: true), ct);
            if (saved.IsFailure) db.RoutingUpgradeSnapshots.Remove(snapshot.Entity);
            return saved.IsSuccess ? Result.Success(saved.Value.Revision ?? "") : Result.Failure<string>(saved.Error);
        }, ct);

    public async Task<Result<string>> UpgradeAsync(Guid definitionId, Guid versionId, RoutingUpgradeInput input, CancellationToken ct)
    {
        return await WorkflowExecutionLock.RunAsync(db, "version:" + versionId, async () =>
        {
            var version = await VersionAsync(definitionId, versionId, ct);
            if (version is null) return Result.Failure<string>(Missing);
            if (RoutingDraftRevision.Of(version) != input.ExpectedRevision) return Result.Failure<string>(new("Workflow.Draft.Conflict", "The draft changed. Review the new upgrade preview."));
            var preview = await PreviewUpgradeAsync(definitionId, versionId, ct);
            if (!preview.Value.CanUpgrade) return Result.Failure<string>(new("Routing.UpgradeBlocked", string.Join(" ", preview.Value.Diagnostics.Select(d => d.Message))));
            var conversion = RoutingUpgrade.Convert(version, await SourcesAsync(version, ct));
            var organizationId = await db.WorkflowDefinitions.Where(d => d.Id == definitionId).Select(d => d.OrganizationId).SingleAsync(ct);
            db.RoutingUpgradeSnapshots.Add(new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                WorkflowVersionId = version.Id,
                Xml = version.XmlContent ?? "",
                DesignerJson = version.DesignerJson,
                WorkspaceJson = version.WorkspaceJson,
                CreatedAt = DateTime.UtcNow
            });
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(version.Id, conversion.Xml, RoutingUpgrade.PrepareDesignerJson(version, conversion.Xml), input.ExpectedRevision, AllowRoutingUpgrade: true), ct);
            return saved.IsSuccess ? Result.Success(saved.Value.Revision ?? "") : Result.Failure<string>(saved.Error);
        }, ct);
    }

    public async Task<Result<IReadOnlyList<RoutingProgress>>> ProgressAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Result.Failure<IReadOnlyList<RoutingProgress>>(new("Workflow.Instance.NotFound", "Workflow instance not found."));
        var version = await versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, ct);
        var rows = await db.RoutingActivations.AsNoTracking().Where(g => g.WorkflowInstanceId == instanceId).OrderBy(g => g.CreatedAt).ToListAsync(ct);
        var activities = await db.ActivityInstances.AsNoTracking().Where(a => a.WorkflowInstanceId == instanceId).ToListAsync(ct);
        var result = rows.Select(g =>
        {
            var states = JsonSerializer.Deserialize<Dictionary<Guid, string>>(g.StatesJson) ?? [];
            var met = states.Count(s => s.Value == "Met"); var node = version?.Activities.FirstOrDefault(n => n.NodeKey == g.JoinNodeKey);
            var branches = states.Select(s => new RoutingBranchProgress(s.Key, activities.Where(a => a.ExecutionTokenId == s.Key)
                .OrderByDescending(a => a.StartedAt).FirstOrDefault()?.Name ?? "Branch", s.Value)).ToList();
            return new RoutingProgress(g.JoinNodeKey, node?.Name ?? g.JoinNodeKey, g.Status,
                g.Status == "Released" ? "Continuation released once; remaining branches are tracked separately."
                    : $"{met} of {states.Count} requirements met. " + string.Join(", ", branches.Where(b => b.Status != "Met").Select(b => b.Name + ": " + b.Status)),
                g.Id, RoutingDefinition.Read(node?.ConfigurationJson)?.WaitFor ?? "All", met, states.Count, branches,
                JsonSerializer.Deserialize<List<string>>(g.SkippedJson) ?? [], g.DeadlineAt, g.ReleasedAt);
        }).ToList();
        return Result.Success<IReadOnlyList<RoutingProgress>>(result);
    }
}
