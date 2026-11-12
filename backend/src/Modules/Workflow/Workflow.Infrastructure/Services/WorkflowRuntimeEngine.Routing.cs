using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;
using Workflow.Application.Routing;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowRuntimeEngine
{
    private int routingAdvances;

    private async Task<WorkflowRoutingScope> RoutingScopeAsync(WorkflowInstance instance, Guid? token, CancellationToken ct)
    {
        var id = token ?? instance.Id;
        var scope = _db.RoutingScopes.Local.FirstOrDefault(s => s.Id == id) ?? await _db.RoutingScopes.FindAsync([id], ct);
        if (scope is not null) return scope;
        var input = (await _variableRepo.GetByInstanceIdAsync(instance.Id, ct))
            .ToDictionary(v => "$input." + v.VariableName, v => JsonSerializer.Deserialize<JsonElement>(v.ValueJson ?? "null"));
        scope = new() { Id = id, OrganizationId = instance.OrganizationId, WorkflowInstanceId = instance.Id, ValuesJson = JsonSerializer.Serialize(input) };
        _db.RoutingScopes.Add(scope); return scope;
    }

    private static Dictionary<string, object?> RoutingValues(WorkflowRoutingScope scope) => JsonSerializer.Deserialize<Dictionary<string, object?>>(scope.ValuesJson) ?? [];

    private async Task<IReadOnlyDictionary<string, object?>> ActivityInputsAsync(WorkflowInstance instance,
        WorkflowVersion version, Guid? token, CancellationToken ct)
    {
        if (!RoutingDefinition.Enabled(version.WorkspaceJson))
            return BuildInputVariables(await _variableRepo.GetByInstanceIdAsync(instance.Id, ct));
        var values = RoutingValues(await RoutingScopeAsync(instance, token, ct));
        foreach (var pair in values.Where(p => p.Key.StartsWith("$input.", StringComparison.Ordinal)).ToList())
            values[pair.Key[7..]] = pair.Value;
        return values;
    }

    private async Task CaptureRoutingResultAsync(WorkflowInstance instance, ActivityInstance activity,
        IReadOnlyDictionary<string, object?> values, string outcome, DateTime now, CancellationToken ct)
    {
        if (_db.RoutingResults.Local.Any(r => r.ActivityInstanceId == activity.Id)
            || await _db.RoutingResults.AnyAsync(r => r.ActivityInstanceId == activity.Id, ct)) return;
        _db.RoutingResults.Add(new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = instance.OrganizationId,
            WorkflowInstanceId = instance.Id,
            ActivityInstanceId = activity.Id,
            ExecutionTokenId = activity.ExecutionTokenId,
            NodeKey = activity.ActivityNodeKey,
            Outcome = outcome,
            ValuesJson = JsonSerializer.Serialize(values),
            CreatedAt = now
        });
        var scope = await RoutingScopeAsync(instance, activity.ExecutionTokenId, ct);
        var data = RoutingValues(scope);
        RoutingData.ReplaceNode(data, activity.ActivityNodeKey, values, outcome);
        var changed = JsonSerializer.Deserialize<HashSet<string>>(scope.ChangedNodesJson) ?? [];
        changed.Add(activity.ActivityNodeKey); scope.ChangedNodesJson = JsonSerializer.Serialize(changed);
        scope.ValuesJson = JsonSerializer.Serialize(data);
    }

    private async Task<Result> RoutingAttentionAsync(WorkflowInstance instance, ActivityDefinition node,
        WorkflowExecutionToken? token, string message, DateTime now, CancellationToken ct, IReadOnlyList<RoutingRuleTrace>? rules = null)
    {
        await _incidentService.OpenAsync(instance.OrganizationId, instance.Id, WorkflowIncidentType.ServiceTaskFailed,
            WorkflowIncidentSeverity.High, message, now, null, node.NodeKey, "Workflow.Routing.Attention", message, ct);
        await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.ActivityFailed, now, node.NodeKey,
            payloadJson: JsonSerializer.Serialize(new { routing = true, message, tokenId = token?.Id, rules }), cancellationToken: ct);
        if (token?.ForkActivityInstanceId is Guid activationId)
        {
            var gate = await _db.RoutingActivations.FindAsync([activationId], ct);
            if (gate is not null)
            {
                var states = JsonSerializer.Deserialize<Dictionary<Guid, string>>(gate.StatesJson) ?? [];
                states[token.Id] = "Unmet"; gate.StatesJson = JsonSerializer.Serialize(states); token.Fail(now);
                var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, ct);
                if (version is not null) await ResolveRoutingGateAsync(instance, version, gate, null, now, ct);
            }
        }
        await _db.SaveChangesAsync(ct); return Result.Success();
    }

    private async Task<Result> AdvanceRoutingGateAsync(WorkflowInstance instance, WorkflowVersion version,
        ActivityDefinition node, WorkflowExecutionToken? token, DateTime now, CancellationToken ct)
    {
        if (++routingAdvances > 2000) return await RoutingAttentionAsync(instance, node, token, "Automatic routing exceeded its execution limit. Check for a loop without a waiting Step or Event.", now, ct);
        if (token?.Status is ExecutionTokenStatus.Cancelled or ExecutionTokenStatus.Failed) return Result.Success();
        var config = RoutingDefinition.Read(node.ConfigurationJson);
        if (config is null) return await RoutingAttentionAsync(instance, node, token, "This routing component is not configured.", now, ct);
        if (node.ActivityType == ActivityType.JoinGateway)
        {
            var gate = token?.ForkActivityInstanceId is Guid activationId ? await _db.RoutingActivations.FindAsync([activationId], ct) : null;
            if (gate is null || gate.JoinNodeKey != node.NodeKey) return await RoutingAttentionAsync(instance, node, token, "Branch reached a Join outside its activation.", now, ct);
            var states = JsonSerializer.Deserialize<Dictionary<Guid, string>>(gate.StatesJson) ?? [];
            if (token is null || !states.ContainsKey(token.Id) || states[token.Id] == "Cancelled") return Result.Success();
            states[token.Id] = "Met"; token.Complete(now); gate.StatesJson = JsonSerializer.Serialize(states);
            return await ResolveRoutingGateAsync(instance, version, gate, token, now, ct);
        }
        var scope = await RoutingScopeAsync(instance, token?.Id, ct);
        var outgoing = version.Transitions.Where(e => e.FromActivityDefinitionId == node.Id).ToList();
        var decision = node.ActivityType == ActivityType.ParallelGateway
            ? new RoutingDecision(outgoing.Where(e => e.TransitionKey != config.FallbackTransitionKey).Select(e => e.TransitionKey).ToList(), [])
            : RoutingEvaluator.Evaluate(config, RoutingValues(scope));
        if (decision.Problem is not null) return await RoutingAttentionAsync(instance, node, token, decision.Problem, now, ct, decision.Rules);
        var execution = await CreateActivityInstanceAsync(instance, node, now, ct); execution.AttachToken(token?.Id); execution.Complete(now);
        await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.ActivityCompleted, now, node.NodeKey,
            payloadJson: JsonSerializer.Serialize(new { routing = true, matched = decision.Matched, selected = decision.Transitions, decision.UsedFallback, rules = decision.Rules }), cancellationToken: ct);
        if (node.ActivityType == ActivityType.ExclusiveGateway || decision.UsedFallback)
            return await FollowRoutingConnectionAsync(instance, version, node, decision.Transitions.Single(), token, now, ct);
        var join = version.Activities.SingleOrDefault(a => a.NodeKey == config.JoinNodeKey && a.ActivityType == ActivityType.JoinGateway);
        if (join is null) return await RoutingAttentionAsync(instance, node, token, "Select the Fork's matching Join.", now, ct);
        var joinConfig = RoutingDefinition.Read(join.ConfigurationJson)!;
        var fresh = RoutingValues(scope);
        RoutingData.ClearNodes(fresh, RoutingGraphRules.Region(RoutingDefinition.Graph(version), node.NodeKey, join.NodeKey).Append(join.NodeKey));
        scope.ValuesJson = JsonSerializer.Serialize(fresh);
        var activation = new WorkflowRoutingActivation
        {
            Id = execution.Id,
            OrganizationId = instance.OrganizationId,
            WorkflowInstanceId = instance.Id,
            WorkflowVersionId = version.Id,
            ForkNodeKey = node.NodeKey,
            JoinNodeKey = join.NodeKey,
            ParentTokenId = token?.Id,
            CreatedAt = now,
            DeadlineAt = joinConfig.DeadlineSeconds is int seconds ? now.AddSeconds(seconds) : null,
            SkippedJson = JsonSerializer.Serialize(outgoing.Where(e => !decision.Transitions.Contains(e.TransitionKey)).Select(e => e.TransitionKey))
        };
        var branches = new List<(WorkflowExecutionToken Token, string Connection)>();
        foreach (var connection in decision.Transitions)
        {
            var branch = WorkflowExecutionToken.Create(instance.OrganizationId, instance.Id, node.NodeKey,
                $"{execution.Id:N}:{branches.Count}", now, join.NodeKey, token?.Id, execution.Id);
            await _tokenRepo.AddAsync(branch, ct);
            _db.RoutingScopes.Add(new() { Id = branch.Id, OrganizationId = instance.OrganizationId, WorkflowInstanceId = instance.Id, ValuesJson = scope.ValuesJson });
            branches.Add((branch, connection));
        }
        activation.StatesJson = JsonSerializer.Serialize(branches.ToDictionary(b => b.Token.Id, _ => "Pending"));
        _db.RoutingActivations.Add(activation); await _db.SaveChangesAsync(ct);
        foreach (var branch in branches)
        {
            if (branch.Token.Status != ExecutionTokenStatus.Active) continue;
            var result = await FollowRoutingConnectionAsync(instance, version, node, branch.Connection, branch.Token, now, ct);
            if (result.IsFailure) return result;
        }
        return Result.Success();
    }

    private async Task<Result> FollowRoutingConnectionAsync(WorkflowInstance instance, WorkflowVersion version,
        ActivityDefinition node, string connection, WorkflowExecutionToken? token, DateTime now, CancellationToken ct)
    {
        var edge = version.Transitions.FirstOrDefault(e => e.FromActivityDefinitionId == node.Id && e.TransitionKey == connection);
        var next = edge is null ? null : version.Activities.FirstOrDefault(a => a.Id == edge.ToActivityDefinitionId);
        if (next is null) return await RoutingAttentionAsync(instance, node, token, "The selected destination no longer exists.", now, ct);
        await RecordTransitionTakenAsync(instance, node.NodeKey, next.NodeKey, edge!, now, ct);
        instance.AdvanceTo(next.NodeKey, now);
        return await AdvanceFromNodeAsync(instance, version, next.NodeKey, now, ct, token);
    }

    private async Task<Result> ResolveRoutingGateAsync(WorkflowInstance instance, WorkflowVersion version,
        WorkflowRoutingActivation gate, WorkflowExecutionToken? arrival, DateTime now, CancellationToken ct)
    {
        var node = version.Activities.Single(a => a.NodeKey == gate.JoinNodeKey);
        var config = RoutingDefinition.Read(node.ConfigurationJson)!;
        var states = JsonSerializer.Deserialize<Dictionary<Guid, string>>(gate.StatesJson) ?? [];
        if (gate.ReleasedAt is not null)
        {
            arrival?.ConsumeJoin(); await _db.SaveChangesAsync(ct);
            return await ResumeRoutingEndAsync(instance, version, now, ct);
        }
        var timedOut = gate.DeadlineAt is DateTime deadline && now >= deadline;
        if (gate.Status is "Attention" or "Cancelled") return Result.Success();
        var impossible = RoutingEvaluator.JoinImpossible(config.WaitFor, states.Values.ToList());
        var ready = RoutingEvaluator.JoinReady(config.WaitFor, states.Values.ToList());
        if (!ready && !timedOut && !impossible) { await _db.SaveChangesAsync(ct); return Result.Success(); }
        if (timedOut || impossible)
        {
            if (gate.Status == "Attention") return Result.Success();
            gate.Status = "Attention";
            await _incidentService.OpenAsync(instance.OrganizationId, instance.Id, WorkflowIncidentType.ServiceTaskFailed,
                WorkflowIncidentSeverity.High, timedOut ? "The Join deadline expired." : "The Join requirements cannot be met.", now,
                null, node.NodeKey, "Workflow.Routing.Join", null, ct);
            if (config.FailureTransitionKey is null) { await _db.SaveChangesAsync(ct); return Result.Success(); }
        }
        gate.ReleasedAt = now; gate.Status = ready && !timedOut ? "Released" : "Failed";
        gate.WinnerTokenId = config.WaitFor == "Any" && ready ? arrival?.Id : null;
        var parent = gate.ParentTokenId is Guid parentId ? await _tokenRepo.GetByIdAsync(parentId, ct) : null;
        if (ready && !timedOut)
        {
            var target = await RoutingScopeAsync(instance, gate.ParentTokenId, ct); var merged = RoutingValues(target);
            var changed = JsonSerializer.Deserialize<HashSet<string>>(target.ChangedNodesJson) ?? [];
            var qualifying = config.WaitFor == "Any" ? new[] { arrival!.Id } : states.Where(p => p.Value == "Met").Select(p => p.Key).ToArray();
            foreach (var branchId in qualifying)
            {
                var branch = await RoutingScopeAsync(instance, branchId, ct);
                var nodes = JsonSerializer.Deserialize<HashSet<string>>(branch.ChangedNodesJson) ?? [];
                RoutingData.Merge(merged, RoutingValues(branch), nodes); changed.UnionWith(nodes);
            }
            var winner = config.WaitFor == "Any" ? await RoutingScopeAsync(instance, arrival!.Id, ct) : null;
            RoutingData.JoinOutput(merged, node.NodeKey, winner is null ? null : RoutingValues(winner), winner is null ? null : JsonSerializer.Deserialize<HashSet<string>>(winner.ChangedNodesJson));
            changed.Add(node.NodeKey);
            target.ValuesJson = JsonSerializer.Serialize(merged);
            target.ChangedNodesJson = JsonSerializer.Serialize(changed);
        }
        if (config.RemainingBranches == "Cancel" || timedOut || impossible)
            await CancelRoutingBranchesAsync(instance, states.Where(p => p.Value == "Pending").Select(p => p.Key).ToList(), now, ct);
        foreach (var branch in await _db.WorkflowExecutionTokens.Where(t => t.ForkActivityInstanceId == gate.Id).ToListAsync(ct))
            if (branch.Status == ExecutionTokenStatus.Completed) branch.ConsumeJoin();
        await _events.AppendAsync(instance.OrganizationId, instance.Id, WorkflowEventType.JoinCompleted, now, node.NodeKey,
            payloadJson: JsonSerializer.Serialize(new { activationId = gate.Id, gate.Status, gate.WinnerTokenId }), cancellationToken: ct);
        // Save the release and downstream creation under the same instance transaction/lock.
        await _db.SaveChangesAsync(ct);
        var connection = gate.Status == "Released"
            ? version.Transitions.Single(e => e.FromActivityDefinitionId == node.Id && e.TransitionKey != config.FailureTransitionKey).TransitionKey
            : config.FailureTransitionKey!;
        return await FollowRoutingConnectionAsync(instance, version, node, connection, parent, now, ct);
    }

    private async Task CancelRoutingBranchesAsync(WorkflowInstance instance, IReadOnlyList<Guid> roots, DateTime now, CancellationToken ct)
    {
        var tokens = await _tokenRepo.GetByInstanceIdAsync(instance.Id, ct); var ids = roots.ToHashSet();
        bool changed;
        do { changed = false; foreach (var token in tokens) if (token.ParentTokenId is Guid parent && ids.Contains(parent)) changed |= ids.Add(token.Id); } while (changed);
        foreach (var token in tokens.Where(t => ids.Contains(t.Id) && t.Status == ExecutionTokenStatus.Active)) token.Cancel(now);
        var wholeInstance = instance.Status == WorkflowInstanceStatus.Cancelled;
        var activities = await _db.ActivityInstances.Where(a => a.WorkflowInstanceId == instance.Id && (wholeInstance || a.ExecutionTokenId != null && ids.Contains(a.ExecutionTokenId.Value))).ToListAsync(ct);
        var activityIds = activities.Select(a => a.Id).ToList();
        // Cancel the called subtree in the same transaction; the tree lock excludes completions.
        var children = await _db.WorkflowInstances.Where(i => i.ParentInstanceId == instance.Id && i.ParentActivityInstanceId != null && activityIds.Contains(i.ParentActivityInstanceId.Value)
            && (i.Status == WorkflowInstanceStatus.Running || i.Status == WorkflowInstanceStatus.Suspended)).ToListAsync(ct);
        foreach (var child in children)
        {
            child.Cancel(now);
            var descendants = await _tokenRepo.GetByInstanceIdAsync(child.Id, ct);
            await CancelRoutingBranchesAsync(child, descendants.Select(t => t.Id).ToList(), now, ct);
            foreach (var work in await _db.WorkItems.Where(w => w.WorkflowInstanceId == child.Id && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed || w.Status == WorkItemStatus.Preparing)).ToListAsync(ct)) work.Cancel(now);
            foreach (var job in await _db.IntegrationJobs.Where(j => j.WorkflowInstanceId == child.Id && (j.Status == "Pending" || j.Status == "Running" || j.Status == "Delivered")).ToListAsync(ct)) job.Cancel();
            foreach (var wait in await _db.EventSubscriptions.Where(w => w.WorkflowInstanceId == child.Id && w.Status == "Waiting").ToListAsync(ct)) wait.Finish("Cancelled");
            foreach (var timer in await _db.WorkflowTimers.Where(t => t.WorkflowInstanceId == child.Id && t.Status == WorkflowTimerStatus.Pending).ToListAsync(ct)) timer.Cancel(now);
        }
        foreach (var activity in activities.Where(a => a.Status == ActivityInstanceStatus.Active)) activity.Skip(now);
        foreach (var item in await _db.WorkItems.Where(w => activityIds.Contains(w.ActivityInstanceId) && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed || w.Status == WorkItemStatus.Preparing)).ToListAsync(ct)) item.Cancel(now);
        foreach (var timer in await _db.WorkflowTimers.Where(t => activityIds.Contains(t.ActivityInstanceId) && t.Status == WorkflowTimerStatus.Pending).ToListAsync(ct)) timer.Cancel(now);
        foreach (var wait in await _db.EventSubscriptions.Where(w => activityIds.Contains(w.ActivityInstanceId) && w.Status == "Waiting").ToListAsync(ct)) wait.Finish("Cancelled");
        foreach (var job in await _db.IntegrationJobs.Where(j => activityIds.Contains(j.ActivityInstanceId) && (j.Status == "Pending" || j.Status == "Running" || j.Status == "Delivered")).ToListAsync(ct)) job.Cancel();
        foreach (var gate in await _db.RoutingActivations.Where(g => g.WorkflowInstanceId == instance.Id).ToListAsync(ct))
        {
            var states = JsonSerializer.Deserialize<Dictionary<Guid, string>>(gate.StatesJson) ?? [];
            foreach (var id in ids) if (states.GetValueOrDefault(id) == "Pending") states[id] = "Cancelled";
            gate.StatesJson = JsonSerializer.Serialize(states);
            if (gate.ParentTokenId is Guid parent && ids.Contains(parent)) { gate.Status = "Cancelled"; gate.ReleasedAt ??= now; }
        }
    }

    private async Task<Result> ResumeRoutingEndAsync(WorkflowInstance instance, WorkflowVersion version, DateTime now, CancellationToken ct)
    {
        var scope = await RoutingScopeAsync(instance, null, ct);
        var values = RoutingValues(scope);
        if (!values.TryGetValue("$pendingEnd", out var raw) || await _db.WorkflowExecutionTokens.AnyAsync(t => t.WorkflowInstanceId == instance.Id && t.Status == ExecutionTokenStatus.Active, ct)) return Result.Success();
        var node = raw?.ToString(); values.Remove("$pendingEnd"); scope.ValuesJson = JsonSerializer.Serialize(values);
        return node is null ? Result.Success() : await AdvanceFromNodeAsync(instance, version, node, now, ct);
    }

    public async Task ProcessRoutingDeadlinesAsync(CancellationToken ct, Func<Guid, CancellationToken, Task>? admit = null)
    {
        var now = DateTime.UtcNow;
        var ids = await _db.RoutingActivations.Where(g => g.Status == "Waiting" && g.DeadlineAt <= now).Select(g => g.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            var gate = await _db.RoutingActivations.FindAsync([id], ct);
            if (gate is null) continue;
            await WorkflowExecutionLock.RunAsync(_db, "instance:" + gate.WorkflowInstanceId, async () =>
            {
                await _db.Entry(gate).ReloadAsync(ct);
                await WorkflowRecoveryAdmission.RequireAsync(admit, gate.OrganizationId, ct);
                var instance = await _instanceRepo.GetByIdAsync(gate.WorkflowInstanceId, ct);
                if (instance?.Status != WorkflowInstanceStatus.Running || gate.Status != "Waiting") return false;
                var version = await LoadPinnedVersionAsync(instance.PinnedWorkflowVersionId, ct);
                if (version is not null) await ResolveRoutingGateAsync(instance, version, gate, null, now, ct);
                return true;
            }, ct);
        }
    }
}
