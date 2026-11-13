using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowMappingRuntime(WorkflowDbContext db, MappingSourceResolver resolver, IServiceProvider services, ILogger<WorkflowMappingRuntime>? logger = null) : IStepMappingRuntime
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, IntegrationJson.Options);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, IntegrationJson.Options)!;
    private static DesignerMappingConfiguration Config(WorkflowMappingInvocation invocation) => invocation.ExecutionKind == "Step"
        ? IntegrationJson.Read<BusinessActivityConfiguration>(invocation.ConfigurationJson).TypedMapping!
        : IntegrationJson.Read<HttpActivityConfiguration>(invocation.ConfigurationJson).TypedMapping!;
    private static IEnumerable<TransformNode> Walk(TransformNode node)
    {
        yield return node;
        foreach (var child in (node.Args ?? []).Concat(node.Properties?.Values.AsEnumerable() ?? []))
            foreach (var descendant in Walk(child)) yield return descendant;
    }
    private static IEnumerable<TransformNode> Expressions(DesignerMappingConfiguration c) => c.Inputs.Select(i => i.Value)
        .Concat(c.Outputs.Select(i => i.Value)).Concat(c.StepBindings.Select(i => i.Value)).Concat(c.Actions.Select(i => i.Inputs));

    internal async Task<Result> BeginGraphAsync(Guid instanceId, Guid activityId, string configuration, string operationKey, CancellationToken ct,
        string? eventNodeKey = null, bool requiredEvent = false)
    {
        if (await db.MappingInvocations.AnyAsync(i => i.OperationKey == operationKey, ct)) return Result.Success();
        var instance = await db.WorkflowInstances.FindAsync([instanceId], ct) ?? throw new InvalidOperationException("Instance is missing.");
        var activity = await db.ActivityInstances.FindAsync([activityId], ct) ?? throw new InvalidOperationException("Activity is missing.");
        var root = await WorkflowTreeGuard.RootAsync(db, instance.Id, ct);
        var rootInstance = await db.WorkflowInstances.FindAsync([root], ct) ?? instance;
        var invocation = WorkflowMappingInvocation.Create(instance.OrganizationId, root, activityId, activity.ExecutionTokenId,
            rootInstance.StartedByUserId ?? throw new UnauthorizedAccessException("Typed mappings require a persisted authorized actor."),
            instanceId, instance.PinnedWorkflowVersionId, eventNodeKey ?? activity.ActivityNodeKey,
            eventNodeKey is null ? WorkspaceDesign.Configuration(configuration)["required"]?.GetValue<bool>() == false ? "OptionalGraph" : "Graph"
                : requiredEvent ? "RequiredEvent" : "OptionalEvent", configuration, operationKey, DateTime.UtcNow);
        db.MappingInvocations.Add(invocation); await db.SaveChangesAsync(ct);
        await PrepareAsync(invocation, ct); return Result.Success();
    }

    internal async Task PrepareAsync(WorkflowMappingInvocation invocation, CancellationToken ct)
    {
        if (invocation.Applied || invocation.ExecutionKind == "Graph" && invocation.GraphAdvanced || invocation.Status is "Cancelled" or "Attention" or "Delivered") return;
        try
        {
            if (!await RunnableAsync(invocation, ct)) return;
            await AuthorizeProvenanceAsync(Read<List<ResolvedMappingSource>>(invocation.SourcesJson), invocation.OrganizationId, invocation.ActorUserId, ct);
            if (invocation.Status == "Waiting") invocation.Retry();
            var waits = new Dictionary<string, WorkflowMappingWait>(StringComparer.Ordinal);
            var previousWaits = Read<List<WorkflowMappingWait>>(invocation.WaitDependenciesJson).ToDictionary(w => w.Key, StringComparer.Ordinal);
            void WaitFor(string key, MissingDataPolicy policy, string reason)
            {
                MappingValidation.Policy(policy);
                waits[key] = previousWaits.GetValueOrDefault(key) ?? new(key, invocation.WindowAnchor, invocation.WindowAnchor.AddSeconds(policy.DeadlineSeconds!.Value), policy.TimeoutOutcome!, reason);
                if (waits.Count > 1000) throw new FormatException("Too many unresolved mapping dependencies.");
            }
            bool Timely(string key, MissingDataPolicy? policy, DateTimeOffset? availableAt)
            {
                if (policy?.Mode != "Wait") return true;
                var deadline = previousWaits.GetValueOrDefault(key)?.Deadline ?? invocation.WindowAnchor.AddSeconds(policy.DeadlineSeconds!.Value);
                return availableAt is not null ? availableAt.Value.UtcDateTime <= deadline : DateTime.UtcNow < deadline;
            }
            var c = Config(invocation);
            foreach (var expression in Expressions(c).Concat(c.Selectors.Select(s => s.KeyValue)).Concat(c.Selectors.Where(s => s.StartedByUserId is not null).Select(s => s.StartedByUserId!)))
                MappingEvaluator.ValidateTree(expression);
            if (c.TreeRoot is not null)
            {
                var rootPin = await db.WorkflowInstances.SingleAsync(i => i.Id == invocation.RootInstanceId, ct);
                if (rootPin.PinnedWorkflowVersionId != c.TreeRoot.WorkflowVersionId
                    || !await db.WorkflowVersions.AnyAsync(v => v.Id == rootPin.PinnedWorkflowVersionId && v.WorkflowDefinitionId == c.TreeRoot.WorkflowDefinitionId, ct))
                    throw new FormatException("Mapping authoring tree does not match the pinned runtime root.");
            }
            if (invocation.TargetInstanceId == Guid.Empty)
            {
                var target = await resolver.TreeTargetAsync(invocation, invocation.TargetVersionId, ct);
                var parent = invocation.ParentInvocationId is Guid parentId ? await db.MappingInvocations.SingleAsync(i => i.Id == parentId && i.OrganizationId == invocation.OrganizationId, ct) : null;
                var policy = parent is null ? null : Config(parent).Actions.SingleOrDefault(a => a.Id == invocation.ActionId)?.UnavailableTarget;
                if (target is null || !Timely("target", policy, target is null ? null : new DateTimeOffset(DateTime.SpecifyKind(target.StartedAt, DateTimeKind.Utc))))
                {
                    Missing(invocation, policy ?? new(), "Target child instance does not exist."); await db.SaveChangesAsync(ct); return;
                }
                invocation.BindTarget(target!.Id);
            }
            invocation.ReplaceWaits([]);
            var instance = await db.WorkflowInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == invocation.TargetInstanceId && i.OrganizationId == invocation.OrganizationId, ct);
            await resolver.AuthorizeAsync(instance, invocation.ActorUserId, invocation.ExecutionKind == "Step", ct);
            if (invocation.ExecutionKind == "MapOnly")
            {
                MappingTypes.ValidateValue(c.InvocationInputs ?? throw new FormatException("Target invocation input schema is required."),
                    Read<JsonElement>(invocation.InvocationInputsJson ?? throw new FormatException("Prepared inputs are missing.")));
                invocation.Complete("{}", DateTime.UtcNow); await db.SaveChangesAsync(ct); return;
            }
            if (invocation.PreparedRequestJson is not null)
            { await QueuePreparedAsync(invocation, instance, ct); await db.SaveChangesAsync(ct); return; }
            var version = await db.WorkflowVersions.IgnoreQueryFilters().SingleAsync(v => v.Id == invocation.TargetVersionId, ct);
            var own = new CallableTarget(version.WorkflowDefinitionId, version.Id, invocation.TargetNodeKey);
            var sources = Read<List<ResolvedMappingSource>>(invocation.SourcesJson);
            var selections = Read<Dictionary<string, FrozenRelatedSelection>>(invocation.SelectionsJson);
            var criteria = Read<List<FrozenRelatedCriteria>>(invocation.CriteriaJson);
            List<MappingSample> Samples() => sources.Where(s => s.State == "Available" && s.Value.ValueKind != JsonValueKind.Undefined).Select(s => new MappingSample(s.Source with { JsonPointer = "" }, s.Value)).ToList();
            async Task<bool> Resolve(IEnumerable<TransformNode> nodes)
            {
                var waiting = false;
                foreach (var expression in nodes.SelectMany(Walk).Where(n => n.Op == "source"))
                {
                    var source = expression.Source! with { JsonPointer = "" };
                    if (source.Kind == "Response" || source.Kind == "EventOutput" && source.WorkflowVersionId == own.WorkflowVersionId && source.NodeKey == own.NodeKey && source.SelectorId is null) continue;
                    var value = sources.SingleOrDefault(s => s.Source == source && s.State == "Available" && s.Value.ValueKind != JsonValueKind.Undefined)
                        ?? await resolver.ResolveAsync(source, new(invocation.OrganizationId, invocation.ActorUserId, invocation.RootInstanceId,
                            invocation.Id, new DateTimeOffset(DateTime.SpecifyKind(invocation.WindowAnchor, DateTimeKind.Utc)), selections), ct);
                    sources.RemoveAll(s => s.Source == source);
                    sources.Add(value);
                    invocation.Bind(Json(criteria), Json(selections), Json(sources));
                    var awaitingSelector = source.SelectorId is string selectorId && c.Selectors.Any(s => s.Id == selectorId && s.Missing?.Mode == "Wait")
                        && (!selections.TryGetValue(selectorId, out var selected) || selected.InstanceId is null);
                    var waitKey = "source:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json(expression))));
                    if (!awaitingSelector && expression.Missing?.Mode == "Wait" && (value.State != "Available"
                        || MappingTypes.Select(value.Value, expression.Source!.JsonPointer).ValueKind == JsonValueKind.Undefined || !Timely(waitKey, expression.Missing, value.CompletedAt)))
                    {
                        WaitFor(waitKey, expression.Missing, "Source was not available within its deadline: " + source.FieldKey);
                        waiting = true;
                    }
                    if (sources.Count > 500 || System.Text.Encoding.UTF8.GetByteCount(Json(sources)) > 2097152) throw new FormatException("Frozen source snapshots exceed 2 MB or 500 fields.");
                }
                return waiting;
            }
            foreach (var selector in c.Selectors)
            {
                var frozen = criteria.SingleOrDefault(s => s.SelectorId == selector.Id);
                if (frozen is null)
                {
                    if (await Resolve(new[] { selector.KeyValue }.Concat(selector.StartedByUserId is null ? [] : [selector.StartedByUserId]))) continue;
                    frozen = MappingSelectorPreparation.Freeze(selector, Samples(), new DateTimeOffset(DateTime.SpecifyKind(invocation.WindowAnchor, DateTimeKind.Utc))); criteria.Add(frozen);
                }
                if (!selections.TryGetValue(selector.Id, out var selection) || selection.InstanceId is null)
                {
                    var selected = await resolver.SelectRelatedAsync(frozen, invocation.OrganizationId, invocation.ActorUserId, ct, invocation.RootInstanceId);
                    if (selected is not null && !Timely("selector:" + selector.Id, selector.Missing, MappingSourceResolver.EligibilityAt(selected, frozen.Status))) selected = null;
                    selections[selector.Id] = new(selected?.Id, JsonSerializer.SerializeToElement(frozen.KeyValue), frozen.StartedByUserId, frozen.WindowAnchor, frozen.Deadline, selected is null ? "Unbound" : "Bound",
                        selected is null ? null : MappingSourceResolver.EligibilityAt(selected, frozen.Status));
                    invocation.Bind(Json(criteria), Json(selections), Json(sources));
                    if (selected is null && selector.Missing?.Mode == "Wait") WaitFor("selector:" + selector.Id, selector.Missing, "Related instance is not registered.");
                    else if (selected is null && selector.Missing?.Mode != "Fallback") throw new FormatException("Related instance is not registered; selector requires attention.");
                }
                else
                {
                    if (selection.InstanceId == invocation.RootInstanceId) throw new FormatException("A previous-instance selector cannot bind its owning root.");
                    var bound = await db.WorkflowInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == selection.InstanceId && i.OrganizationId == invocation.OrganizationId, ct);
                    await resolver.AuthorizeAsync(bound, invocation.ActorUserId, false, ct);
                    var eligibility = selection.EligibleAt ?? MappingSourceResolver.EligibilityAt(bound, frozen.Status);
                    if (selector.Missing?.Mode == "Wait" && (eligibility is null || !Timely("selector:" + selector.Id, selector.Missing, eligibility)))
                        WaitFor("selector:" + selector.Id, selector.Missing!, "Related instance did not become eligible within its deadline.");
                }
            }
            await Resolve(Expressions(c));
            invocation.Bind(Json(criteria), Json(selections), Json(sources));
            invocation.ReplaceWaits(waits.Values.ToList());
            if (invocation.Status == "Waiting") { await db.SaveChangesAsync(ct); return; }
            if (invocation.ExecutionKind == "Step")
            {
                var values = c.StepBindings.ToDictionary(b => b.Id, b => MappingRequestPreparation.EvaluateTyped(b.Value, b.Schema, Samples()));
                invocation.PrepareStep(Json(values), Json(sources), DateTime.UtcNow);
                var item = await db.WorkItems.SingleAsync(w => w.ActivityInstanceId == invocation.OriginActivityId, ct);
                item.MappingReady();
            }
            else
            {
                var request = invocation.PreparedRequestJson is null ? MappingRequestPreparation.Prepare(c, Samples()) : Read<PreparedMappingRequest>(invocation.PreparedRequestJson);
                invocation.Prepare(Json(request), Json(sources));
                await QueuePreparedAsync(invocation, instance, ct);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or UnauthorizedAccessException && !WorkflowExecutionLock.IsTransient(db, ex))
        { invocation.Attention(ex.Message); await db.SaveChangesAsync(ct); }
    }

    private async Task QueuePreparedAsync(WorkflowMappingInvocation invocation, WorkflowInstance instance, CancellationToken ct)
    {
        if (invocation.ExecutionKind == "Graph" && invocation.GraphAdvanced) throw new InvalidOperationException("The graph already advanced; no new delivery is permitted.");
        if (await db.IntegrationJobs.AnyAsync(j => j.InvocationId == invocation.Id, ct)) return;
        var http = IntegrationJson.Read<HttpActivityConfiguration>(invocation.ConfigurationJson);
        var connection = await db.IntegrationConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == http.ConnectionId && x.OrganizationId == invocation.OrganizationId && x.Kind == "Http", ct);
        if (connection is null) throw new UnauthorizedAccessException("Mapping connection is unavailable.");
        var job = WorkflowIntegrationJob.Create(invocation.OrganizationId, instance.Id, invocation.OriginActivityId, http.ConnectionId, "Http", invocation.ConfigurationJson, "{}", invocation.OperationKey);
        if (invocation.ExecutionKind is "RequiredEvent" or "OptionalEvent")
            job.ConfigureEvent(invocation.ExecutionKind == "RequiredEvent", WorkspaceDesign.Binding(invocation.ConfigurationJson)?.Trigger ?? "OnEnter", invocation.TargetNodeKey);
        else if (invocation.ExecutionKind == "OptionalGraph") job.ConfigureEvent(false, "InFlow", invocation.TargetNodeKey);
        job.AttachInvocation(invocation.Id, invocation.ExecutionKind); job.SetEventNode(invocation.TargetNodeKey); db.IntegrationJobs.Add(job);
    }

    internal async Task<bool> CanDeliverAsync(WorkflowMappingInvocation invocation, CancellationToken ct)
    {
        if (invocation.ExecutionKind == "Graph" && invocation.GraphAdvanced || !await RunnableAsync(invocation, ct) || invocation.Status == "Attention") return false;
        try
        {
            var target = await db.WorkflowInstances.SingleAsync(i => i.Id == invocation.TargetInstanceId, ct);
            await resolver.AuthorizeAsync(target, invocation.ActorUserId, false, ct);
            await AuthorizeProvenanceAsync(Read<List<ResolvedMappingSource>>(invocation.SourcesJson), invocation.OrganizationId, invocation.ActorUserId, ct);
            return true;
        }
        catch (UnauthorizedAccessException ex) { invocation.Attention(ex.Message); await db.SaveChangesAsync(ct); return false; }
    }

    private Task<IReadOnlyList<ResolvedMappingSource>> AuthorizeProvenanceAsync(IEnumerable<ResolvedMappingSource> sources, Guid organizationId, Guid actor, CancellationToken ct)
        => resolver.AuthorizeProvenanceAsync(sources, organizationId, actor, ct);

    private static void Missing(WorkflowMappingInvocation invocation, MissingDataPolicy policy, string reason)
    {
        if (policy.Mode != "Wait") throw new FormatException(reason);
        MappingValidation.Policy(policy);
        var deadline = invocation.WindowAnchor.AddSeconds(policy.DeadlineSeconds!.Value);
        invocation.ReplaceWaits([new("target", invocation.WindowAnchor, deadline, policy.TimeoutOutcome!, reason)]);
    }

    internal async Task<bool> RunnableAsync(WorkflowMappingInvocation invocation, CancellationToken ct)
    {
        var root = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.Id == invocation.RootInstanceId && i.OrganizationId == invocation.OrganizationId, ct);
        if (root is null || root.Status == WorkflowInstanceStatus.Cancelled)
        { invocation.Cancel(); await db.SaveChangesAsync(ct); return false; }
        if (invocation.OriginTokenId is Guid token && await db.WorkflowExecutionTokens.IgnoreQueryFilters().AnyAsync(t => t.Id == token && t.Status == ExecutionTokenStatus.Cancelled, ct))
        { invocation.Cancel(); await db.SaveChangesAsync(ct); return false; }
        var paused = root.Status is WorkflowInstanceStatus.Suspended or WorkflowInstanceStatus.Failed;
        async Task<bool> CheckAncestry(Guid instanceId)
        {
            var target = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == instanceId, ct);
            for (var depth = 0; depth < 17; depth++)
            {
                if (target.OrganizationId != invocation.OrganizationId) throw new UnauthorizedAccessException("Invalid invocation tenant.");
                if (target.Status == WorkflowInstanceStatus.Cancelled) { invocation.Cancel(); await db.SaveChangesAsync(ct); return false; }
                if (target.Status is WorkflowInstanceStatus.Suspended or WorkflowInstanceStatus.Failed) paused = true;
                if (target.ParentInstanceId is not Guid parent) return target.Id == invocation.RootInstanceId;
                target = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == parent, ct);
            }
            throw new FormatException("Invocation tree exceeds the runtime depth limit.");
        }
        var origin = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == invocation.OriginActivityId && a.OrganizationId == invocation.OrganizationId, ct);
        return await CheckAncestry(origin.WorkflowInstanceId) && (invocation.TargetInstanceId == Guid.Empty || await CheckAncestry(invocation.TargetInstanceId)) && !paused;
    }

    internal async Task ApplyDeliveryAsync(WorkflowIntegrationJob job, IntegrationResult response, IWorkflowRuntimeEngine engine, CancellationToken ct, WorkflowActivityEvents? activityEvents = null)
    {
        var invocation = await db.MappingInvocations.SingleAsync(i => i.Id == job.InvocationId, ct);
        // Another worker may have loaded this receipt before acquiring the owning root lock.
        await db.Entry(invocation).ReloadAsync(ct);
        if (invocation.ExecutionKind == "Graph" && invocation.GraphAdvanced && !invocation.Applied)
        { job.Cancel(); await db.SaveChangesAsync(ct); return; }
        if (!await RunnableAsync(invocation, ct))
        { if (invocation.Status == "Cancelled") { job.Cancel(); await db.SaveChangesAsync(ct); } return; }
        if (invocation.Applied)
        {
            await AdvanceGraphAsync(invocation, engine, ct);
            job.Complete(invocation.OutputsJson ?? "{}", response.StatusCode); await db.SaveChangesAsync(ct); return;
        }
        invocation.RecordResponse(job.DeliveryResultJson ?? Json(response)); await db.SaveChangesAsync(ct);
        if (!response.Success)
        {
            await FailExecutionAsync(invocation, job, response.Error ?? "Transport failed.", response, engine, activityEvents, ct); return;
        }
        try
        {
            var c = Config(invocation);
            var version = await db.WorkflowVersions.SingleAsync(v => v.Id == invocation.TargetVersionId, ct);
            var own = new CallableTarget(version.WorkflowDefinitionId, version.Id, invocation.TargetNodeKey);
            var receipts = Read<List<ResolvedMappingSource>>(invocation.SourcesJson);
            var samples = receipts.Where(s => s.State == "Available" && s.Value.ValueKind != JsonValueKind.Undefined).Select(s => new MappingSample(s.Source, s.Value)).ToList();
            JsonElement body;
            try { body = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(response.Body) ? "null" : response.Body); }
            catch (JsonException) { body = JsonSerializer.SerializeToElement(response.Body); }
            if (c.Endpoint?.Response is not null) MappingTypes.ValidateValue(c.Endpoint.Response with { Required = true }, body);
            samples.Add(new(new(own.WorkflowDefinitionId, own.WorkflowVersionId, own.NodeKey, "Response", "$response"), body));
            var targets = new List<CallableTargetDescriptor>();
            // Catalog identity is the pinned target, while invocation identity remains parent/action.
            foreach (var target in c.Actions.Select(a => a.Target).Distinct())
            {
                var node = await db.ActivityDefinitions.SingleOrDefaultAsync(n => n.WorkflowVersionId == target.WorkflowVersionId && n.NodeKey == target.NodeKey, ct);
                var targetConfig = IntegrationJson.Read<HttpActivityConfiguration>(node?.ConfigurationJson);
                if (node?.ActivityType != ActivityType.ServiceTask || node.ActionKey != "http.request" || targetConfig.Protocol != "Rest"
                    || targetConfig.MappingMode != "Typed" || targetConfig.TypedMapping?.Callable != true || targetConfig.TypedMapping.InvocationInputs is null)
                    throw new FormatException("Follow-up target is not a callable typed HTTP event.");
                targets.Add(new(target, node!.Name, [], targetConfig.TypedMapping.InvocationInputs, true));
            }
            var actionSources = new Dictionary<string, IReadOnlyList<ResolvedMappingSource>>(StringComparer.Ordinal);
            IReadOnlyList<ResolvedMappingSource> Dependencies(IReadOnlyList<SourceReference> refs) => refs.Select(r =>
                receipts.SingleOrDefault(s => s.Source == r) ?? new ResolvedMappingSource("Available", default, invocation.TargetInstanceId,
                    null, null, invocation.Id, new DateTimeOffset(DateTime.UtcNow), r)).Select(s => s with { Value = default }).ToList();
            var requestSources = new HashSet<SourceReference>();
            MappingRequestPreparation.Prepare(c, samples, r => requestSources.Add(r));
            receipts.Add(new("Available", default, invocation.TargetInstanceId, null, null, invocation.Id, new DateTimeOffset(DateTime.UtcNow),
                new(own.WorkflowDefinitionId, own.WorkflowVersionId, own.NodeKey, "Response", "$response"), Dependencies: Dependencies(requestSources.ToList())));
            var complete = MappingRequestPreparation.Complete(c, samples, new([], targets), own, (kind, id, refs) =>
            {
                var dependencies = Dependencies(refs);
                if (kind == "Action") actionSources.Add(id, dependencies);
                else receipts.Add(new("Available", default, invocation.TargetInstanceId, null, null, invocation.Id, new DateTimeOffset(DateTime.UtcNow),
                    new(own.WorkflowDefinitionId, own.WorkflowVersionId, own.NodeKey, "EventOutput", id), Dependencies: dependencies));
            });
            var children = new List<WorkflowMappingInvocation>();
            foreach (var action in complete.Actions ?? [])
            {
                if (await db.MappingInvocations.AnyAsync(i => i.ParentInvocationId == invocation.Id && i.ActionId == action.ActionId, ct)) continue;
                var target = await resolver.TreeTargetAsync(invocation, action.Target.WorkflowVersionId, ct);
                if (target is not null) await resolver.AuthorizeAsync(target, invocation.ActorUserId, false, ct);
                var node = await db.ActivityDefinitions.SingleAsync(n => n.WorkflowVersionId == action.Target.WorkflowVersionId && n.NodeKey == action.Target.NodeKey, ct);
                var depth = 0; var ancestor = invocation;
                while (true)
                {
                    if (action.Mode == "Run" && ancestor.TargetVersionId == action.Target.WorkflowVersionId && ancestor.TargetNodeKey == action.Target.NodeKey)
                        throw new FormatException("Recursive invocation chain is not allowed.");
                    if (ancestor.ParentInvocationId is not Guid parent) break;
                    if (++depth >= 32) throw new FormatException("Invocation chain exceeds 32 actions.");
                    ancestor = await db.MappingInvocations.SingleAsync(i => i.Id == parent, ct);
                }
                var child = WorkflowMappingInvocation.Create(invocation.OrganizationId, invocation.RootInstanceId, invocation.OriginActivityId,
                    invocation.OriginTokenId, invocation.ActorUserId, target?.Id ?? Guid.Empty, action.Target.WorkflowVersionId, action.Target.NodeKey,
                    action.Mode == "MapOnly" ? "MapOnly" : "Callable", node.ConfigurationJson!, invocation.Id + ":" + action.ActionId,
                    DateTime.UtcNow, invocation.Id, action.ActionId, Json(action.Inputs));
                child.Bind("[]", "{}", Json(actionSources[action.ActionId]));
                if (target is null)
                {
                    if (action.UnavailableTarget?.Mode == "Wait") Missing(child, action.UnavailableTarget, "Target child instance does not exist.");
                    else child.Attention("Target child instance does not exist.");
                }
                children.Add(child);
            }
            // The owning root transaction commits the output receipt and all action identities together.
            db.MappingInvocations.AddRange(children);
            invocation.Bind(invocation.CriteriaJson, invocation.SelectionsJson, Json(receipts));
            invocation.Complete(Json(complete.Outputs), DateTime.UtcNow);
            job.Complete(invocation.OutputsJson!, response.StatusCode);
            await db.SaveChangesAsync(ct);
            await AdvanceGraphAsync(invocation, engine, ct);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or UnauthorizedAccessException && !WorkflowExecutionLock.IsTransient(db, ex))
        {
            if (invocation.Applied)
            { invocation.Attention(ex.Message); job.Fail("Graph application: " + ex.Message, response.StatusCode, null); await db.SaveChangesAsync(ct); }
            else await FailExecutionAsync(invocation, job, "Output application: " + ex.Message, response, engine, activityEvents, ct);
        }
    }

    private async Task FailExecutionAsync(WorkflowMappingInvocation invocation, WorkflowIntegrationJob job, string error,
        IntegrationResult response, IWorkflowRuntimeEngine engine, WorkflowActivityEvents? activityEvents, CancellationToken ct)
    {
        invocation.Attention(error); job.Fail(error, response.StatusCode, null);
        if (invocation.ExecutionKind is "RequiredEvent" or "OptionalEvent" or "OptionalGraph" && job.EventTrigger != "OnFailure")
        {
            var events = activityEvents ?? services.GetRequiredService<WorkflowActivityEvents>();
            var execution = await db.ActivityInstances.SingleAsync(a => a.Id == invocation.OriginActivityId, ct);
            var instance = await db.WorkflowInstances.SingleAsync(i => i.Id == execution.WorkflowInstanceId, ct);
            var definition = await db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId && a.NodeKey == execution.ActivityNodeKey, ct);
            var queued = await events.QueueAsync(instance, definition, execution, "OnFailure", job.Id.ToString("N"), ct);
            if (queued.IsFailure)
            {
                var diagnostic = error + " OnFailure dispatch failed: " + queued.Error.Code + ": " + queued.Error.Message;
                invocation.Attention(diagnostic); job.Fail(diagnostic, response.StatusCode, null);
                logger?.LogError("OnFailure dispatch for invocation {InvocationId} failed: {Error}", invocation.Id, queued.Error.Message);
            }
        }
        if (invocation.ExecutionKind == "Graph" && !invocation.GraphAdvanced)
        {
            var http = IntegrationJson.Read<HttpActivityConfiguration>(invocation.ConfigurationJson);
            var failed = await engine.CompleteExternalActivityAsync(invocation.OriginActivityId, new Dictionary<string, object?>(),
                response.TimedOut ? http.TimeoutOutcome : http.ErrorOutcome, error, DateTime.UtcNow, ct);
            if (failed.IsFailure) throw new InvalidOperationException(failed.Error.Message);
            invocation.MarkGraphAdvanced();
        }
        await db.SaveChangesAsync(ct);
    }

    internal async Task RecoverAsync(CancellationToken ct, Func<Guid, CancellationToken, Task>? admit = null)
    {
        var cancelled = await db.MappingInvocations.Where(i => i.Status != "Cancelled" && !i.Applied
            && db.WorkflowInstances.Any(r => r.Id == i.RootInstanceId && r.Status == WorkflowInstanceStatus.Cancelled)).ToListAsync(ct);
        foreach (var invocation in cancelled)
        {
            await WorkflowExecutionLock.RunAsync(db, "instance:" + invocation.RootInstanceId, async () =>
            {
                await db.Entry(invocation).ReloadAsync(ct);
                await WorkflowRecoveryAdmission.RequireAsync(admit, invocation.OrganizationId, ct);
                if (!invocation.Applied && await db.WorkflowInstances.AnyAsync(r => r.Id == invocation.RootInstanceId && r.Status == WorkflowInstanceStatus.Cancelled, ct))
                    invocation.Cancel();
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        var cancelledSteps = await db.WorkItems.Where(w => w.Status == WorkItemStatus.Preparing
            && db.MappingInvocations.Any(i => i.OriginActivityId == w.ActivityInstanceId && i.ExecutionKind == "Step" && i.Status == "Cancelled")).ToListAsync(ct);
        foreach (var item in cancelledSteps)
        {
            await WorkflowExecutionLock.RunAsync(db, "instance:" + item.WorkflowInstanceId, async () =>
            {
                await db.Entry(item).ReloadAsync(ct);
                await WorkflowRecoveryAdmission.RequireAsync(admit, item.OrganizationId, ct);
                var progress = item.FormProgressJson is null ? null : Read<ActivityFormProgress>(item.FormProgressJson);
                if (item.Status == WorkItemStatus.Preparing && await db.MappingInvocations.AnyAsync(i => i.OriginActivityId == item.ActivityInstanceId
                    && i.ExecutionKind == "Step" && i.FormVisitId == (progress == null ? item.Id : progress.VisitId) && i.Status == "Cancelled", ct)) item.Cancel(DateTime.UtcNow);
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        await db.SaveChangesAsync(ct);
        var ids = await db.MappingInvocations.AsNoTracking().Where(i => i.Status != "Cancelled" && (i.Status == "Preparing" || i.Status == "Waiting"
            || i.Status == "Prepared" && !db.IntegrationJobs.Any(j => j.InvocationId == i.Id)
            || i.Applied && i.Status != "Attention" && i.ExecutionKind == "Graph" && !i.GraphAdvanced))
            .OrderBy(i => i.UpdatedAt).Select(i => i.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                var invocation = await db.MappingInvocations.FindAsync([id], ct);
                if (invocation is null) continue;
                await WorkflowExecutionLock.RunAsync(db, "instance:" + invocation.RootInstanceId, async () =>
                {
                    await db.Entry(invocation).ReloadAsync(ct);
                    await WorkflowRecoveryAdmission.RequireAsync(admit, invocation.OrganizationId, ct);
                    invocation.Touch();
                    if (!await RunnableAsync(invocation, ct)) return false;
                    if (invocation.Applied)
                    { await AdvanceGraphAsync(invocation, services.GetRequiredService<IWorkflowRuntimeEngine>(), ct); return true; }
                    if (invocation.Status == "Waiting") invocation.Retry();
                    await PrepareAsync(invocation, ct);
                    if (invocation.Status == "Waiting" && invocation.Deadline <= DateTime.UtcNow)
                    {
                        invocation.Attention("Mapping wait expired.");
                        if (invocation.ExecutionKind == "Graph" && !invocation.GraphAdvanced)
                        {
                            var timed = await services.GetRequiredService<IWorkflowRuntimeEngine>().CompleteExternalActivityAsync(invocation.OriginActivityId,
                                new Dictionary<string, object?>(), invocation.TimeoutOutcome ?? "timeout", "Mapping wait expired.", DateTime.UtcNow, ct);
                            if (timed.IsSuccess) invocation.MarkGraphAdvanced();
                        }
                        await db.SaveChangesAsync(ct);
                    }
                    return true;
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
            {
                // The root transaction has rolled back. Discard its tracked graph/receipt mutations before
                // recording the failure in a fresh transaction; one poison receipt must not starve the batch.
                db.ChangeTracker.Clear();
                // Tenant offboarding is a batch boundary, not a malformed mapping or a fallback.
                if (ex is WorkflowTenantAdmissionRefusedException) throw;
                var transient = WorkflowExecutionLock.IsTransient(db, ex);
                var deterministic = ex is FormatException or JsonException or InvalidOperationException or UnauthorizedAccessException;
                if (transient) logger?.LogWarning(ex, "Mapping invocation {InvocationId} will retry on a later recovery poll.", id);
                else logger?.LogError(ex, "Mapping invocation {InvocationId} recovery failed.", id);
                if (!transient && !deterministic) throw;
                try
                {
                    var failed = await db.MappingInvocations.SingleOrDefaultAsync(i => i.Id == id, ct);
                    if (failed is null) continue;
                    await WorkflowExecutionLock.RunAsync(db, "instance:" + failed.RootInstanceId, async () =>
                    {
                        await db.Entry(failed).ReloadAsync(ct);
                        await WorkflowRecoveryAdmission.RequireAsync(admit, failed.OrganizationId, ct);
                        if (failed.Status != "Cancelled" && !(failed.Applied && (failed.ExecutionKind != "Graph" || failed.GraphAdvanced)))
                        {
                            if (transient) failed.DeferRecovery("Transient recovery failure; retry pending: " + ex.Message);
                            else { failed.Attention("Recovery failed: " + ex.Message); failed.Touch(); }
                        }
                        await db.SaveChangesAsync(ct);
                        return true;
                    }, ct);
                }
                catch (Exception recordingError) when (recordingError is not OperationCanceledException && !ct.IsCancellationRequested && WorkflowExecutionLock.IsTransient(db, recordingError))
                {
                    logger?.LogWarning(recordingError, "Could not record recovery diagnostic for {InvocationId}; persisted work remains retryable.", id);
                    db.ChangeTracker.Clear();
                }
            }
        }
    }
    private async Task AdvanceGraphAsync(WorkflowMappingInvocation invocation, IWorkflowRuntimeEngine engine, CancellationToken ct)
    {
        if (invocation.ExecutionKind != "Graph" || invocation.GraphAdvanced) return;
        var outputs = Read<Dictionary<string, JsonElement>>(invocation.OutputsJson ?? "{}").ToDictionary(p => p.Key, p => (object?)p.Value);
        var applied = await engine.CompleteExternalActivityAsync(invocation.OriginActivityId, outputs, "success", null, DateTime.UtcNow, ct);
        if (applied.IsFailure) throw new InvalidOperationException(applied.Error.Message);
        invocation.MarkGraphAdvanced();
        var job = await db.IntegrationJobs.SingleOrDefaultAsync(j => j.InvocationId == invocation.Id, ct);
        if (job is not null) job.Complete(invocation.OutputsJson ?? "{}", job.StatusCode);
        await db.SaveChangesAsync(ct);
    }
}
