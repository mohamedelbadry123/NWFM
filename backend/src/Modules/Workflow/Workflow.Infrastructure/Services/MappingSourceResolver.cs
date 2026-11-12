using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class MappingSourceResolver(WorkflowDbContext db, IWorkflowActorAccess actors, IFormGateway forms,
    IWorkflowTaskTypeCatalog? taskTypes = null) : IMappingSourceResolver
{
    private async Task<WorkflowActorAccess> AuthorizePinAsync(Guid definitionId, Guid versionId, Guid organizationId, Guid actor, bool form, CancellationToken ct)
    {
        var access = await actors.GetAsync(actor, ct);
        var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId && v.WorkflowDefinitionId == definitionId, ct);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(d => d.Id == definitionId && d.OrganizationId == organizationId, ct);
        if (access is null || version is null || definition is null || !access.HasPermission(NwfmPolicies.ViewInstances)
            || form && !access.HasPermission(NwfmPolicies.ViewSubmissions)
            || !DesignerMappingCatalog.Allowed(WorkflowWorkspaceDefinition.Read(version.WorkspaceJson), access.Scope, access.Hierarchy))
            throw new UnauthorizedAccessException("Mapping source pin is unavailable or forbidden for the persisted actor.");
        return access;
    }
    internal async Task AuthorizeAsync(WorkflowInstance instance, Guid actor, bool form, CancellationToken ct)
    {
        var access = await actors.GetAsync(actor, ct);
        var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == instance.PinnedWorkflowVersionId, ct);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == version.WorkflowDefinitionId, ct);
        var settings = WorkflowWorkspaceDefinition.Read(version.WorkspaceJson);
        if (definition.OrganizationId != instance.OrganizationId || access is null
            || !access.HasPermission(NwfmPolicies.ViewInstances)
            || form && !access.HasPermission(NwfmPolicies.ViewSubmissions)
            || !DesignerMappingCatalog.Allowed(settings, access.Scope, access.Hierarchy)
            || !instance.Location.IsCoveredBy(access.Scope, settings?.DepartmentCode, settings?.FieldActivityCode))
            throw new UnauthorizedAccessException("Mapping source access was denied for the persisted actor.");
    }

    internal async Task AuthorizeSourceAsync(WorkflowInstance instance, SourceReference source, Guid actor, CancellationToken ct)
    {
        await AuthorizeAsync(instance, actor, source.Kind == "FormField", ct);
        if (instance.PinnedWorkflowVersionId != source.WorkflowVersionId) throw new UnauthorizedAccessException("Source version differs from its instance.");
        var access = await AuthorizePinAsync(source.WorkflowDefinitionId, source.WorkflowVersionId, instance.OrganizationId, actor, source.Kind == "FormField", ct);
        if (source.Kind != "FormField") return;
        var node = await db.ActivityDefinitions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.WorkflowVersionId == source.WorkflowVersionId && n.NodeKey == source.NodeKey, ct);
        var business = IntegrationJson.Read<BusinessActivityConfiguration>(node?.ConfigurationJson);
        var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == source.WorkflowVersionId, ct);
        var settings = WorkflowWorkspaceDefinition.Read(version.WorkspaceJson);
        var form = source.FormId is Guid formId ? await forms.FindFieldActivityFormAsync(formId, ct) : null;
        var pinned = source.FormStepId is null ? business.FormDefinitionId == source.FormId && business.FormVersionNo == source.FormVersion
            : business.FormSteps.Any(f => f.Id == source.FormStepId && f.FormId == source.FormId && f.VersionNo == source.FormVersion);
        if (node is null || form is null || !pinned || business.TaskTypeId is not Guid taskType || taskTypes is null
            || !(await taskTypes.FormIdsAsync(taskType, ct)).Contains(form.Id)
            || !instance.Location.IsCoveredBy(access.Scope, business.DepartmentCode ?? settings?.DepartmentCode, business.FieldActivityCode ?? settings?.FieldActivityCode)
            || !instance.Location.IsCoveredBy(access.Scope, form.DepartmentCode ?? settings?.DepartmentCode, form.FieldActivityCode ?? settings?.FieldActivityCode))
            throw new UnauthorizedAccessException("Form and Step classification must be authorized at the actual source instance location.");
    }

    internal static DateTimeOffset? EligibilityAt(WorkflowInstance instance, string? status)
    {
        if (status is not null && instance.Status.ToString() != status) return null;
        var at = status switch
        {
            null => instance.StartedAt,
            "Completed" => instance.CompletedAt,
            "Cancelled" => instance.CancelledAt,
            "Suspended" => instance.SuspendedAt,
            _ => instance.StatusEnteredAt
        };
        return at is DateTime value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
    }

    internal async Task<WorkflowInstance?> SelectRelatedAsync(FrozenRelatedCriteria criteria, Guid organizationId, Guid actor, CancellationToken ct, Guid? owningRootId = null)
    {
        var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v => v.Id == criteria.WorkflowVersionId && v.WorkflowDefinitionId == criteria.WorkflowDefinitionId, ct);
        if (version is null) throw new FormatException("Unknown related workflow pin.");
        await AuthorizePinAsync(criteria.WorkflowDefinitionId, criteria.WorkflowVersionId, organizationId, actor, false, ct);
        var query = db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().Where(i => i.OrganizationId == organizationId && i.PinnedWorkflowVersionId == criteria.WorkflowVersionId && i.ParentInstanceId == null && i.Id != owningRootId);
        query = criteria.KeyKind == "BusinessKey" ? query.Where(i => i.BusinessEntityId == criteria.KeyValue) : query.Where(i => i.CorrelationId == criteria.KeyValue);
        if (criteria.StartedByUserId is Guid starter) query = query.Where(i => i.StartedByUserId == starter);
        if (criteria.Status is not null)
        {
            var status = Enum.Parse<WorkflowInstanceStatus>(criteria.Status); query = query.Where(i => i.Status == status);
        }
        if (criteria.WindowStart is DateTimeOffset start)
        {
            var lower = start.UtcDateTime; var upper = criteria.WindowAnchor.UtcDateTime;
            query = query.Where(i => i.StartedAt >= lower && i.StartedAt <= upper);
        }
        if (criteria.Deadline is DateTimeOffset deadline)
        {
            var upper = deadline.UtcDateTime;
            query = query.Where(i => i.StartedAt <= upper);
            if (criteria.Status is not null)
                query = query.Where(i => (i.Status == WorkflowInstanceStatus.Completed ? i.CompletedAt
                    : i.Status == WorkflowInstanceStatus.Cancelled ? i.CancelledAt
                    : i.Status == WorkflowInstanceStatus.Suspended ? i.SuspendedAt : i.StatusEnteredAt) <= upper);
        }
        var matches = await query.OrderByDescending(i => i.StartedAt).ThenBy(i => i.Id).Take(2).ToListAsync(ct);
        // Do not convert forbidden matches into missing/fallback, or select a different authorized row.
        foreach (var match in matches) await AuthorizeAsync(match, actor, false, ct);
        if (matches.Count > 1 && criteria.MultipleMatches != "Latest") throw new FormatException("Multiple related instances require attention.");
        return matches.FirstOrDefault();
    }

    internal async Task<WorkflowInstance?> TreeTargetAsync(WorkflowMappingInvocation invocation, Guid versionId, CancellationToken ct)
    {
        var origin = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == invocation.OriginActivityId, ct);
        var current = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == origin.WorkflowInstanceId, ct);
        var ancestors = new List<WorkflowInstance>();
        for (var depth = 0; depth < 17; depth++)
        {
            ancestors.Add(current);
            if (current.ParentInstanceId is not Guid parent) break;
            current = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == parent && i.OrganizationId == invocation.OrganizationId, ct);
        }
        var ancestor = ancestors.SingleOrDefault(i => i.PinnedWorkflowVersionId == versionId);
        if (ancestor is not null) return ancestor;
        var anchors = new Dictionary<Guid, ActivityInstance> { [origin.WorkflowInstanceId] = origin };
        foreach (var child in ancestors.Where(a => a.ParentInstanceId is not null && a.ParentActivityInstanceId is not null))
            anchors[child.ParentInstanceId!.Value] = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == child.ParentActivityInstanceId && a.WorkflowInstanceId == child.ParentInstanceId, ct);
        var cutoffs = anchors.ToDictionary(p => p.Key, p => p.Value.StartedAt);
        var candidates = new List<WorkflowInstance>();
        var frontier = new List<WorkflowInstance> { ancestors[^1] };
        for (var depth = 0; frontier.Count > 0 && depth < 17; depth++)
        {
            var ids = frontier.Select(i => i.Id).ToArray();
            var children = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().Where(i => i.OrganizationId == invocation.OrganizationId && i.ParentInstanceId != null && ids.Contains(i.ParentInstanceId.Value)).Take(1001).ToListAsync(ct);
            if (children.Count > 1000) throw new FormatException("Runtime tree exceeds mapping limits.");
            frontier = [];
            foreach (var child in children)
            {
                if (child.ParentActivityInstanceId is not Guid activityId) continue;
                var parentActivity = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == activityId && a.WorkflowInstanceId == child.ParentInstanceId, ct);
                if (anchors.TryGetValue(parentActivity.WorkflowInstanceId, out var anchor))
                {
                    // Exclude another visit of the origin's slot, not a different sibling Task Type.
                    if (parentActivity.ActivityNodeKey == anchor.ActivityNodeKey
                        && ancestors.Any(a => a.ParentInstanceId == child.ParentInstanceId && a.Id != child.Id)) continue;
                    if (!await VisibleAsync(parentActivity, anchor, ct)) continue;
                }
                else if (parentActivity.StartedAt > cutoffs[parentActivity.WorkflowInstanceId]) continue;
                cutoffs[child.Id] = anchors.TryGetValue(child.Id, out var inside) ? inside.StartedAt : cutoffs[parentActivity.WorkflowInstanceId];
                frontier.Add(child);
                if (child.PinnedWorkflowVersionId == versionId) candidates.Add(child);
            }
        }
        if (candidates.Count > 1)
        {
            var parentIds = candidates.Select(c => c.ParentActivityInstanceId).ToArray();
            var parentVisits = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().Where(a => parentIds.Contains(a.Id)).ToListAsync(ct);
            if (parentVisits.Select(a => (a.WorkflowInstanceId, a.ExecutionTokenId)).Distinct().Count() != 1)
                throw new FormatException("Child instances have ambiguous branch lineage.");
            var latest = parentVisits.OrderByDescending(a => a.StartedAt).ThenBy(a => a.Id).First();
            return candidates.Single(c => c.ParentActivityInstanceId == latest.Id);
        }
        return candidates.SingleOrDefault();
    }

    private async Task<bool> VisibleAsync(ActivityInstance candidate, ActivityInstance origin, CancellationToken ct)
    {
        if (candidate.Id == origin.Id) return true;
        var token = origin.ExecutionTokenId; var cutoff = origin.StartedAt;
        for (var depth = 0; depth < 33; depth++)
        {
            if (candidate.ExecutionTokenId == token) return candidate.StartedAt <= cutoff;
            if (candidate.ExecutionTokenId is Guid candidateToken)
            {
                var branchToken = await db.WorkflowExecutionTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == candidateToken, ct);
                for (var nesting = 0; nesting < 32; nesting++)
                {
                    if (branchToken.ParentTokenId == token && branchToken.ForkActivityInstanceId is Guid gateId)
                    {
                        var gate = await db.RoutingActivations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(g => g.Id == gateId, ct);
                        if (gate?.Status == "Released" && gate.ReleasedAt <= cutoff && candidate.StartedAt <= gate.ReleasedAt
                            && (gate.WinnerTokenId is null || gate.WinnerTokenId == branchToken.Id)
                            && JsonSerializer.Deserialize<Dictionary<Guid, string>>(gate.StatesJson)!.GetValueOrDefault(branchToken.Id) == "Met") return true;
                    }
                    if (branchToken.ParentTokenId is not Guid parent) break;
                    branchToken = await db.WorkflowExecutionTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == parent, ct);
                }
            }
            if (token is not Guid id) return false;
            var branch = await db.WorkflowExecutionTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == id, ct);
            cutoff = branch.CreatedAt; token = branch.ParentTokenId;
        }
        throw new FormatException("Mapping token ancestry exceeds limits.");
    }

    private async Task<DateTime> CommonAncestorCutoffAsync(WorkflowInstance source, ActivityInstance origin, CancellationToken ct)
    {
        var anchors = new Dictionary<Guid, DateTime> { [origin.WorkflowInstanceId] = origin.StartedAt };
        var current = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == origin.WorkflowInstanceId, ct);
        for (var depth = 0; current.ParentInstanceId is Guid parent && depth < 17; depth++)
        {
            var activation = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == current.ParentActivityInstanceId && a.WorkflowInstanceId == parent, ct);
            anchors[parent] = activation.StartedAt;
            current = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == parent, ct);
        }
        for (var depth = 0; depth < 17; depth++)
        {
            if (anchors.TryGetValue(source.Id, out var cutoff)) return cutoff;
            if (source.ParentInstanceId is not Guid parent) break;
            source = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == parent, ct);
        }
        throw new UnauthorizedAccessException("Source and origin do not share a bounded runtime tree.");
    }

    private async Task<WorkflowInstance?> RelatedDescendantAsync(WorkflowInstance root, SourceReference source,
        Guid? bound, Guid actor, CancellationToken ct)
    {
        var lineage = new Dictionary<Guid, Guid?> { [root.PinnedWorkflowVersionId] = null };
        var pending = new Queue<Guid>(); pending.Enqueue(root.PinnedWorkflowVersionId);
        while (pending.TryDequeue(out var id))
        {
            if (lineage.Count > 100) throw new FormatException("Related definition tree exceeds limits.");
            var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == id, ct);
            var pins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}")!;
            var nodes = await db.ActivityDefinitions.IgnoreQueryFilters().AsNoTracking().Where(n => n.WorkflowVersionId == id && n.ActivityType == ActivityType.MainActivity).ToListAsync(ct);
            foreach (var node in nodes)
            {
                var config = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
                var pin = pins.GetValueOrDefault(node.NodeKey, config.VersionId ?? Guid.Empty);
                if (pin == Guid.Empty) continue;
                var child = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v => v.Id == pin, ct);
                var settings = WorkflowWorkspaceDefinition.Read(child?.WorkspaceJson);
                if (child is null || settings?.ParentDefinitionId != version.WorkflowDefinitionId || settings.ParentNodeKey != node.NodeKey || settings.TaskTypeId != config.TaskTypeId)
                    throw new UnauthorizedAccessException("Invalid pinned related definition lineage.");
                if (!lineage.TryAdd(pin, id)) throw new FormatException("Ambiguous or cyclic related definition lineage.");
                pending.Enqueue(pin);
            }
        }
        if (!lineage.ContainsKey(source.WorkflowVersionId)) throw new UnauthorizedAccessException("Source version is outside the selected related definition tree.");
        var path = new HashSet<Guid>(); Guid? cursor = source.WorkflowVersionId;
        while (cursor is Guid id) { if (!path.Add(id) || path.Count > 17) throw new FormatException("Related definition depth exceeded."); cursor = lineage[id]; }
        var frontier = new List<WorkflowInstance> { root }; var matches = new List<WorkflowInstance>(); var seen = new HashSet<Guid>();
        for (var depth = 0; frontier.Count > 0; depth++)
        {
            if (depth > 16) throw new FormatException("Related runtime tree exceeds depth limit.");
            var next = new List<WorkflowInstance>();
            foreach (var parent in frontier)
            {
                if (!seen.Add(parent.Id) || seen.Count > 1000) throw new FormatException("Related runtime tree is cyclic or excessive.");
                await AuthorizeAsync(parent, actor, false, ct);
                if (parent.PinnedWorkflowVersionId == source.WorkflowVersionId && (bound is null || parent.Id == bound)) matches.Add(parent);
                var version = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == parent.PinnedWorkflowVersionId, ct);
                var pins = JsonSerializer.Deserialize<Dictionary<string, Guid>>(version.PinnedChildVersionsJson ?? "{}")!;
                var children = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().Where(i => i.ParentInstanceId == parent.Id && i.OrganizationId == root.OrganizationId).Take(1001).ToListAsync(ct);
                if (children.Count > 1000) throw new FormatException("Related runtime tree exceeds limits.");
                foreach (var child in children)
                {
                    if (!path.Contains(child.PinnedWorkflowVersionId)) continue;
                    var activation = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == child.ParentActivityInstanceId && a.WorkflowInstanceId == parent.Id, ct);
                    if (activation is null) throw new UnauthorizedAccessException("Invalid related child activation lineage.");
                    var node = await db.ActivityDefinitions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.WorkflowVersionId == version.Id && n.NodeKey == activation.ActivityNodeKey && n.ActivityType == ActivityType.MainActivity, ct);
                    var config = IntegrationJson.Read<BusinessActivityConfiguration>(node?.ConfigurationJson);
                    var childVersion = await db.WorkflowVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == child.PinnedWorkflowVersionId, ct);
                    var settings = WorkflowWorkspaceDefinition.Read(childVersion.WorkspaceJson);
                    if (node is null || pins.GetValueOrDefault(node.NodeKey, config.VersionId ?? Guid.Empty) != childVersion.Id
                        || settings?.ParentDefinitionId != version.WorkflowDefinitionId || settings.ParentNodeKey != node.NodeKey || settings.TaskTypeId != config.TaskTypeId)
                        throw new UnauthorizedAccessException("Related child does not match the selected root's pinned definition lineage.");
                    next.Add(child);
                }
            }
            frontier = next;
        }
        if (matches.Count > 1) throw new FormatException("Related child has multiple visits or branches; attention is required.");
        if (bound is not null && matches.Count == 0) throw new UnauthorizedAccessException("Bound related descendant is outside the selected tree.");
        return matches.SingleOrDefault();
    }

    internal async Task<IReadOnlyList<ResolvedMappingSource>> AuthorizeProvenanceAsync(IEnumerable<ResolvedMappingSource> sources, Guid organizationId, Guid actor, CancellationToken ct)
    {
        var result = new List<ResolvedMappingSource>();
        async Task Visit(IEnumerable<ResolvedMappingSource> entries, int depth)
        {
            if (depth > 32) throw new UnauthorizedAccessException("Mapping provenance exceeds its lineage bound.");
            foreach (var source in entries)
            {
                if (result.Count >= 10000) throw new UnauthorizedAccessException("Mapping provenance exceeds its size bound.");
                if (source.InstanceId is Guid id)
                {
                    var instance = await db.WorkflowInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == id && i.OrganizationId == organizationId, ct);
                    await AuthorizeSourceAsync(instance, source.Source, actor, ct);
                }
                result.Add(source with { Value = default, Dependencies = null });
                if (source.Dependencies is not null) await Visit(source.Dependencies, depth + 1);
            }
        }
        await Visit(sources, 0); return result;
    }

    public async Task<ResolvedMappingSource> ResolveAsync(SourceReference source, MappingResolutionContext context, CancellationToken ct)
    {
        var invocation = await db.MappingInvocations.IgnoreQueryFilters().SingleAsync(i => i.Id == context.InvocationId && i.OrganizationId == context.OrganizationId, ct);
        if (invocation.ActorUserId != context.ActorUserId || invocation.RootInstanceId != context.RootInstanceId) throw new UnauthorizedAccessException("Invalid mapping security context.");
        var access = await AuthorizePinAsync(source.WorkflowDefinitionId, source.WorkflowVersionId, context.OrganizationId, context.ActorUserId, source.Kind == "FormField", ct);
        WorkflowInstance? instance = null;
        var priorSources = JsonSerializer.Deserialize<List<ResolvedMappingSource>>(invocation.SourcesJson, IntegrationJson.Options)!;
        var priorSource = priorSources.SingleOrDefault(s => s.Source == source);
        Guid? activityPin = priorSource?.ActivityInstanceId; Guid? formVisitPin = priorSource?.FormVisitId; Guid? invocationPin = priorSource?.InvocationId;
        ResolvedMappingSource Missing() => new("Missing", default, instance?.Id, null, null, invocationPin, null, source, activityPin, formVisitPin);
        WorkflowMappingInvocation? lineage = null;
        if (source.Kind == "EventOutput" && source.SelectorId is null)
        {
            var parent = invocation.ParentInvocationId; var seen = new HashSet<Guid>();
            while (parent is Guid id)
            {
                if (!seen.Add(id) || seen.Count > 32) throw new FormatException("Invalid invocation ancestry.");
                var ancestor = await db.MappingInvocations.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id && i.OrganizationId == context.OrganizationId, ct);
                if (ancestor.TargetVersionId == source.WorkflowVersionId && ancestor.TargetNodeKey == source.NodeKey)
                { lineage = ancestor; break; }
                parent = ancestor.ParentInvocationId;
            }
        }
        if (source.SelectorId is string selector)
        {
            if (!context.Selections.TryGetValue(selector, out var selection) || selection.InstanceId is not Guid selected) return Missing();
            var configuration = invocation.ExecutionKind == "Step" ? IntegrationJson.Read<BusinessActivityConfiguration>(invocation.ConfigurationJson).TypedMapping
                : IntegrationJson.Read<HttpActivityConfiguration>(invocation.ConfigurationJson).TypedMapping;
            var selectorPin = configuration?.Selectors.SingleOrDefault(s => s.Id == selector) ?? throw new UnauthorizedAccessException("Unknown related selector.");
            var selectedRoot = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.Id == selected && i.OrganizationId == context.OrganizationId && i.ParentInstanceId == null, ct);
            if (selectedRoot is null || selectedRoot.PinnedWorkflowVersionId != selectorPin.WorkflowVersionId)
                throw new UnauthorizedAccessException("Selected related root does not match its pinned selector.");
            await AuthorizePinAsync(selectorPin.WorkflowDefinitionId, selectorPin.WorkflowVersionId, context.OrganizationId, context.ActorUserId, false, ct);
            var bound = priorSources.FirstOrDefault(s => s.Source.SelectorId == selector && s.Source.WorkflowVersionId == source.WorkflowVersionId && s.InstanceId is not null)?.InstanceId;
            instance = await RelatedDescendantAsync(selectedRoot, source, bound, context.ActorUserId, ct);
        }
        else
        {
            var bound = lineage?.TargetInstanceId
                ?? priorSources.FirstOrDefault(s => s.Source.SelectorId is null && s.Source.WorkflowVersionId == source.WorkflowVersionId && s.InstanceId is not null)?.InstanceId
                ?? (invocation.TargetVersionId == source.WorkflowVersionId && invocation.TargetInstanceId != Guid.Empty ? invocation.TargetInstanceId : (Guid?)null);
            instance = bound is Guid id ? await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id && i.OrganizationId == context.OrganizationId, ct)
                : await TreeTargetAsync(invocation, source.WorkflowVersionId, ct);
        }
        if (instance is null) return Missing();
        if (instance.OrganizationId != context.OrganizationId || instance.PinnedWorkflowVersionId != source.WorkflowVersionId
            || !await db.WorkflowVersions.IgnoreQueryFilters().AnyAsync(v => v.Id == source.WorkflowVersionId && v.WorkflowDefinitionId == source.WorkflowDefinitionId, ct))
            throw new UnauthorizedAccessException("Source identity does not match its runtime pin.");
        await AuthorizeSourceAsync(instance, source, context.ActorUserId, ct);
        ResolvedMappingSource Value(JsonElement value, Guid? submission = null, Guid? result = null, Guid? call = null, DateTime? completed = null)
            => new("Available", value.Clone(), instance.Id, submission, result, call,
                completed is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null, source, activityPin, formVisitPin);
        async Task<ResolvedMappingSource> Produced(JsonElement value, WorkflowMappingInvocation producer)
        {
            var receipts = JsonSerializer.Deserialize<List<ResolvedMappingSource>>(producer.SourcesJson, IntegrationJson.Options)!;
            var output = receipts.SingleOrDefault(s => s.Source.Kind == "EventOutput" && s.Source.NodeKey == producer.TargetNodeKey
                && s.Source.WorkflowVersionId == producer.TargetVersionId && s.Source.FieldKey == source.FieldKey);
            var dependencies = output?.Dependencies ?? receipts.Where(s => s.InvocationId != producer.Id).Select(s => s with { Value = default }).ToList();
            await AuthorizeProvenanceAsync(dependencies, context.OrganizationId, context.ActorUserId, ct);
            return Value(value, call: producer.Id, completed: producer.CompletedAt) with { Dependencies = dependencies };
        }
        if (source.Kind == "WorkflowInput")
        {
            var scope = await db.RoutingScopes.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == instance.Id, ct);
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(scope?.ValuesJson ?? "{}")!;
            return values.TryGetValue("$input." + source.FieldKey, out var value) ? Value(value, completed: instance.StartedAt) : Missing();
        }
        if (source.Kind == "InvocationInput")
        {
            var dependencies = priorSources.Where(s => s.Source.Kind != "InvocationInput").Select(s => s with { Value = default }).ToList();
            await AuthorizeProvenanceAsync(dependencies, context.OrganizationId, context.ActorUserId, ct);
            return invocation.InvocationInputsJson is null ? Missing() : Value(JsonSerializer.Deserialize<JsonElement>(invocation.InvocationInputsJson), call: invocation.Id,
                completed: invocation.WindowAnchor) with
            { Dependencies = dependencies };
        }
        // Explicit invocation ancestry wins over graph results; never read the latest independent call.
        if (lineage is not null)
        {
            if (!lineage.Applied) return Missing();
            var outputs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lineage.OutputsJson ?? "{}")!;
            return outputs.TryGetValue(source.FieldKey, out var value) ? await Produced(value, lineage) : Missing();
        }
        var origin = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == invocation.OriginActivityId, ct);
        if (source.SelectorId is null && instance.Id != origin.WorkflowInstanceId)
        {
            var sourceOriginInstance = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == origin.WorkflowInstanceId, ct);
            for (var depth = 0; sourceOriginInstance.ParentInstanceId is Guid parent && depth < 17; depth++)
            {
                if (sourceOriginInstance.ParentActivityInstanceId is Guid parentActivity)
                {
                    var anchorActivity = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == parentActivity, ct);
                    if (parent == instance.Id) { origin = anchorActivity; break; }
                }
                sourceOriginInstance = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == parent, ct);
            }
        }
        var sourceCutoff = source.SelectorId is null && instance.Id != origin.WorkflowInstanceId ? await CommonAncestorCutoffAsync(instance, origin, ct) : origin.StartedAt;
        if (source.Kind == "EventOutput")
        {
            var events = await db.MappingInvocations.IgnoreQueryFilters().AsNoTracking().Where(i => i.TargetInstanceId == instance.Id && i.TargetNodeKey == source.NodeKey
                && (i.ExecutionKind == "RequiredEvent" || i.ExecutionKind == "OptionalEvent") && (invocationPin == null || i.Id == invocationPin))
                .OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id).Take(1001).ToListAsync(ct);
            if (events.Count > 1000) throw new FormatException("Event source visit limit exceeded.");
            var eligible = new List<WorkflowMappingInvocation>();
            foreach (var candidate in events)
            {
                var parentActivity = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == candidate.OriginActivityId, ct);
                if (activityPin is Guid fixedActivity && parentActivity.Id != fixedActivity) continue;
                if (source.SelectorId is null && instance.Id != origin.WorkflowInstanceId && parentActivity.StartedAt > sourceCutoff) continue;
                if (source.SelectorId is not null || instance.Id != origin.WorkflowInstanceId || await VisibleAsync(parentActivity, origin, ct)) eligible.Add(candidate);
            }
            if (eligible.Select(i => i.OriginTokenId).Distinct().Count() > 1 && (source.SelectorId is not null || instance.Id != origin.WorkflowInstanceId))
                throw new FormatException("Event source has ambiguous branch lineage.");
            var captured = eligible.FirstOrDefault();
            if (captured is not null)
            {
                activityPin = captured.OriginActivityId; invocationPin = captured.Id;
                if (!captured.Applied) return Missing();
                var outputs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(captured.OutputsJson ?? "{}")!;
                return outputs.TryGetValue(source.FieldKey, out var value) ? await Produced(value, captured) : Missing();
            }
        }
        var visits = await db.ActivityInstances.IgnoreQueryFilters().AsNoTracking().Where(a => a.WorkflowInstanceId == instance.Id && a.ActivityNodeKey == source.NodeKey)
            .OrderByDescending(a => a.StartedAt).ThenBy(a => a.Id).Take(1001).ToListAsync(ct);
        if (visits.Count > 1000) throw new FormatException("Source visit limit exceeded.");
        var visible = new List<ActivityInstance>();
        foreach (var visit in visits)
            if (source.SelectorId is not null || instance.Id != origin.WorkflowInstanceId && visit.StartedAt <= sourceCutoff
                || instance.Id == origin.WorkflowInstanceId && await VisibleAsync(visit, origin, ct)) visible.Add(visit);
        // A related or different instance cannot borrow an arbitrary fork's latest result.
        if (visible.Select(a => a.ExecutionTokenId).Distinct().Count() > 1 && (source.SelectorId is not null || instance.Id != origin.WorkflowInstanceId))
            throw new FormatException("Source has ambiguous branch lineage.");
        var execution = activityPin is Guid pinnedActivity ? visible.SingleOrDefault(a => a.Id == pinnedActivity) : visible.FirstOrDefault();
        if (execution is null) return Missing();
        activityPin = execution.Id;
        if (source.Kind == "FormField")
        {
            if (source.FormId is not Guid formId || source.FormVersion is not int formVersion) throw new FormatException("Form source requires exact pins.");
            var node = await db.ActivityDefinitions.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.WorkflowVersionId == source.WorkflowVersionId && n.NodeKey == source.NodeKey, ct);
            var config = IntegrationJson.Read<BusinessActivityConfiguration>(node.ConfigurationJson);
            var pinned = source.FormStepId is null ? config.FormDefinitionId == formId && config.FormVersionNo == formVersion
                : config.FormSteps.Any(s => s.Id == source.FormStepId && s.FormId == formId && s.VersionNo == formVersion);
            var info = await forms.FindFieldActivityFormAsync(formId, ct);
            if (!pinned || info is null || config.TaskTypeId is not Guid type || taskTypes is null || !(await taskTypes.FormIdsAsync(type, ct)).Contains(formId)
                || !DesignerMappingCatalog.Allowed(WorkflowWorkspaceDefinition.Read((await db.WorkflowVersions.IgnoreQueryFilters().SingleAsync(v => v.Id == source.WorkflowVersionId, ct)).WorkspaceJson), access.Scope, access.Hierarchy, info.DepartmentCode, info.FieldActivityCode))
                throw new UnauthorizedAccessException("The pinned form source is unavailable or forbidden.");
            var item = await db.WorkItems.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(w => w.ActivityInstanceId == execution.Id, ct);
            if (item is null) return Missing();
            var progress = item.FormProgressJson is null ? null : JsonSerializer.Deserialize<ActivityFormProgress>(item.FormProgressJson);
            if (formVisitPin is null && progress is not null && progress.ActiveStepId == source.FormStepId && progress.ExitNodeKey is null) formVisitPin = progress.VisitId;
            var visit = progress?.Visits.LastOrDefault(v => v.StepId == source.FormStepId && v.FormId == formId && v.VersionNo == formVersion && (formVisitPin is null || v.VisitId == formVisitPin));
            if (source.FormStepId is not null && visit is null) return Missing();
            formVisitPin = visit?.VisitId;
            if (visit is null && item.Status != WorkItemStatus.Completed) return Missing();
            var recorded = visit is null
                ? await forms.GetByClientIdAsync(formId, item.CompletedFormReceiptKey ?? item.Id, "WorkItem", item.Id.ToString(), ct)
                : await forms.GetByClientIdAsync(formId, visit.ReceiptKey ?? visit.VisitId, "WorkItem", item.Id + ":" + visit.VisitId, ct);
            if (recorded is null || recorded.VersionNo != formVersion || visit is not null && recorded.SubmissionId != visit.SubmissionId) return Missing();
            var completedBy = visit?.ActorId ?? item.CompletedByUserId;
            if (completedBy is Guid owner && owner != Guid.Empty && !string.IsNullOrWhiteSpace(recorded.SubmittedBy)
                && (!Guid.TryParse(recorded.SubmittedBy, out var submittedBy) || submittedBy != owner))
                throw new UnauthorizedAccessException("The form receipt was not submitted by the completion actor.");
            if (!recorded.Answers.TryGetValue(source.FieldKey, out var answer)) return Missing();
            var fieldInfo = (await forms.GetFieldsAsync(formId, formVersion, ct)).SingleOrDefault(f => f.DataName == source.FieldKey);
            var schema = fieldInfo is null ? null : DesignerMappingCatalog.FieldSchema(fieldInfo.FieldType);
            if (schema is null) throw new FormatException("Pinned form field has no supported type.");
            var value = JsonSerializer.SerializeToElement(answer);
            // FormGateway deliberately exposes SQL JSON columns as text. Decode only schema-declared structures.
            if (schema.Type is "array" or "object" && value.ValueKind == JsonValueKind.String)
                value = JsonSerializer.Deserialize<JsonElement>(value.GetString()!);
            MappingTypes.ValidateValue(schema, value);
            return Value(value, recorded.SubmissionId, completed: recorded.SubmittedDate?.UtcDateTime);
        }
        var typed = await db.MappingInvocations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => (i.ExecutionKind == "Graph" || i.ExecutionKind == "OptionalGraph") && i.OriginActivityId == execution.Id && i.Applied, ct);
        if (typed is not null)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(typed.OutputsJson ?? "{}")!;
            return values.TryGetValue(source.FieldKey, out var value) ? await Produced(value, typed) : Missing();
        }
        if (source.Kind == "EventOutput")
        {
            var legacy = await db.IntegrationJobs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(j => j.ActivityInstanceId == execution.Id
                && j.InvocationId == null && j.Status == "Completed" && (!j.IsActivityEvent || j.EventTrigger == "InFlow" && j.EventNodeKey == source.NodeKey), ct);
            if (legacy is not null)
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(legacy.ResultJson ?? "{}")!;
                return values.TryGetValue(source.FieldKey, out var value) ? Value(value, result: legacy.Id, completed: legacy.UpdatedAt) : Missing();
            }
        }
        var result = await db.RoutingResults.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.ActivityInstanceId == execution.Id, ct);
        if (result is null) return Missing();
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(result.ValuesJson)!;
        return fields.TryGetValue(source.FieldKey, out var field) ? Value(field, result: result.Id, completed: result.CreatedAt) : Missing();
    }
}
