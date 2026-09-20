using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Commands.ClaimWorkItem;
using Workflow.Application.Commands.CompleteWorkItem;
using Workflow.Application.Commands.CreateWorkflowDefinition;
using Workflow.Application.Commands.CreateWorkflowDraft;
using Workflow.Application.Commands.SaveWorkflowDraftXml;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed class WorkflowWorkspace(WorkflowDbContext db, ICurrentTenant tenant, ISender sender,
    IWorkflowRuntimeEngine engine, IWorkflowReferenceData references, IWorkflowGroupDirectory groups,
    IWorkItemDtoAssembler assembler, IWorkflowVersionRepository versions, WorkflowActivityEvents events) : IWorkflowWorkspace
{
    private static Error Invalid(string message) => new("Workflow.Workspace.Invalid", message);

    public Task<Result<WorkspaceCreated>> CreateAsync(WorkspaceDefinitionInput input, Guid actor, CancellationToken ct) =>
        WorkflowExecutionLock.RunAsync(db, "workspace-create:" + tenant.OrganizationId, async () =>
        {
            if (actor == Guid.Empty || string.IsNullOrWhiteSpace(input.Name)) return Result.Failure<WorkspaceCreated>(Invalid("A name and signed-in workflow participant are required."));
            var settings = input.Settings;
            if (settings.Kind is not ("Main" or "Child")) return Result.Failure<WorkspaceCreated>(Invalid("Select Main or Child workflow."));
            if (settings.Kind == "Main" && !await references.IsValidGeographyAsync(new(settings.ClusterCode ?? "", settings.RegionCode ?? "", settings.CityCode ?? ""), ct))
                return Result.Failure<WorkspaceCreated>(Invalid("Select a valid cluster, region and city."));
            if (settings.Kind == "Child") settings = new("Child");
            var created = await sender.Send(new CreateWorkflowDefinitionCommand(tenant.OrganizationId, "workflow-" + Guid.NewGuid().ToString("N"), input.Name, input.NameAr, null, null), ct);
            if (created.IsFailure) return Result.Failure<WorkspaceCreated>(created.Error);
            var draft = await sender.Send(new CreateWorkflowDraftCommand(created.Value.Id, actor), ct);
            if (draft.IsFailure) return Result.Failure<WorkspaceCreated>(draft.Error);
            XNamespace ns = "https://privora.io/workflow/v1";
            var xml = new XElement(ns + "Workflow", new XAttribute("workspaceJson", JsonSerializer.Serialize(settings, IntegrationJson.Options)),
                new XElement(ns + "Activities", new XElement(ns + "Activity", new XAttribute("nodeKey", "start"), new XAttribute("type", "Start"), new XAttribute("name", "Start")),
                    new XElement(ns + "Activity", new XAttribute("nodeKey", "end"), new XAttribute("type", "End"), new XAttribute("name", "End"))), new XElement(ns + "Transitions"));
            var saved = await sender.Send(new SaveWorkflowDraftXmlCommand(draft.Value.Id, xml.ToString()), ct);
            return saved.IsSuccess ? Result.Success(new WorkspaceCreated(created.Value.Id, draft.Value.Id)) : Result.Failure<WorkspaceCreated>(saved.Error);
        }, ct);

    public Task<IReadOnlyList<WorkspaceWorkflow>> CatalogAsync(CancellationToken ct) => CatalogForKindAsync("Main", ct);
    public Task<IReadOnlyList<WorkspaceWorkflow>> ChildrenAsync(CancellationToken ct) => CatalogForKindAsync("Child", ct);
    private async Task<IReadOnlyList<WorkspaceWorkflow>> CatalogForKindAsync(string kind, CancellationToken ct)
    {
        var rows = await (from v in db.WorkflowVersions.AsNoTracking() join d in db.WorkflowDefinitions on v.WorkflowDefinitionId equals d.Id
            where d.IsActive && v.Status == WorkflowVersionStatus.Published && v.WorkspaceJson != null
            select new WorkspaceWorkflow(d.Id, d.Name, d.NameAr, v.Id, v.VersionNumber, v.WorkspaceJson, d.DefinitionKey)).ToListAsync(ct);
        return rows.Where(x => JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(x.WorkspaceJson!, IntegrationJson.Options)?.Kind == kind)
            .GroupBy(x => x.Id).Select(g => g.MaxBy(x => x.VersionNumber)!).OrderBy(x => x.Name).ToList();
    }

    public Task<Result<Guid>> StartAsync(Guid definitionId, WorkspaceStartInput input, Guid actor, bool administrator, CancellationToken ct) =>
        WorkflowExecutionLock.RunAsync(db, "workspace-start:" + tenant.OrganizationId, async () =>
        {
            if (input.RequestId == Guid.Empty || actor == Guid.Empty || input.IsDemo && !administrator) return Result.Failure<Guid>(Invalid("A request identifier and authorized participant are required."));
            var existing = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.IdempotencyKey == "workspace:" + input.RequestId, ct);
            if (existing is not null)
            {
                var original = await db.WorkflowVersions.FindAsync([existing.PinnedWorkflowVersionId], ct);
                return original?.WorkflowDefinitionId == definitionId && existing.StartedByUserId == actor && existing.IsDemo == input.IsDemo
                    ? Result.Success(existing.Id) : Result.Failure<Guid>(Invalid("This request identifier was already used for a different start."));
            }
            var workflow = (await CatalogAsync(ct)).FirstOrDefault(x => x.Id == definitionId);
            if (workflow is null) return Result.Failure<Guid>(Invalid("Select a published main workflow."));
            var scope = JsonSerializer.Deserialize<WorkflowWorkspaceDefinition>(workflow.WorkspaceJson!, IntegrationJson.Options)!;
            if (!await references.IsValidGeographyAsync(new(scope.ClusterCode!, scope.RegionCode!, scope.CityCode!), ct)) return Result.Failure<Guid>(Invalid("This workflow's geography is no longer active."));
            var screen = "workspace:" + definitionId + (input.IsDemo ? ":demo" : "");
            var binding = await db.WorkflowBindings.FirstOrDefaultAsync(b => b.ScreenKey == screen, ct);
            if (binding is null)
            {
                binding = WorkflowBinding.Create(definitionId, tenant.OrganizationId, "Standalone", input.IsDemo ? "DemoWorkflowRequest" : "WorkflowRequest", "RequestSubmitted", DateTime.UtcNow,
                    "Managed workspace binding", WorkflowBindingMode.Active, screenKey: screen);
                if (input.IsDemo) binding.MarkDemo();
                binding.Activate(DateTime.UtcNow); db.WorkflowBindings.Add(binding); await db.SaveChangesAsync(ct);
            }
            var started = await engine.StartAsync(tenant.OrganizationId, binding.Id, input.RequestId.ToString(), "workspace:" + input.RequestId,
                DateTime.UtcNow, input.Reference, actor, pinnedWorkflowVersionId: workflow.VersionId, cancellationToken: ct);
            return started.IsSuccess ? Result.Success(started.Value.Id) : Result.Failure<Guid>(started.Error);
        }, ct);

    public async Task<IReadOnlyList<WorkspaceInstanceSummary>> ListAsync(string? search, CancellationToken ct)
    {
        var query = from i in db.WorkflowInstances.AsNoTracking() join v in db.WorkflowVersions on i.PinnedWorkflowVersionId equals v.Id
            join d in db.WorkflowDefinitions on v.WorkflowDefinitionId equals d.Id where i.ParentInstanceId == null
            select new { i.Id, d.Name, i.Status, i.CorrelationId, i.StartedAt, i.IsDemo, i.GeographyJson };
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(i => i.Name.Contains(search) || i.CorrelationId != null && i.CorrelationId.Contains(search));
        return (await query.OrderByDescending(i => i.StartedAt).Take(200).ToListAsync(ct))
            .Select(i => new WorkspaceInstanceSummary(i.Id, i.Name, i.Status.ToString(), i.CorrelationId, i.StartedAt, i.IsDemo, i.GeographyJson)).ToList();
    }

    private async Task<bool> ValidDemoActorAsync(WorkflowInstance root, Guid? demoActorId, bool administrator, CancellationToken ct) =>
        demoActorId is null || administrator && root.IsDemo && await db.Participants.AnyAsync(p => p.UserId == demoActorId && p.IsActive && p.IsDemo, ct);

    public async Task<Result<WorkspaceDetail>> GetAsync(Guid id, Guid actor, bool administrator, Guid? demoActorId, CancellationToken ct)
    {
        var root = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (root is null) return Result.Failure<WorkspaceDetail>(Invalid("Instance not found."));
        if (!await ValidDemoActorAsync(root, demoActorId, administrator, ct)) return Result.Failure<WorkspaceDetail>(Invalid("Demo user selection is allowed only for demo instances and seeded demo users."));
        var effective = demoActorId ?? actor;
        var all = new List<WorkflowInstance> { root };
        var frontier = new List<Guid> { root.Id };
        for (var depth = 0; frontier.Count > 0 && depth < 16; depth++)
        {
            var children = await db.WorkflowInstances.Where(i => i.ParentInstanceId != null && frontier.Contains(i.ParentInstanceId.Value)).ToListAsync(ct);
            all.AddRange(children); frontier = children.Select(i => i.Id).ToList();
        }
        var tree = new List<WorkspaceExecution>();
        var groupNames = await db.AssignmentGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        foreach (var instance in all)
        {
            var version = await versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, ct);
            var name = await db.WorkflowDefinitions.Where(d => d.Id == version!.WorkflowDefinitionId).Select(d => d.Name).FirstAsync(ct);
            var activityRows = await db.ActivityInstances.Where(a => a.WorkflowInstanceId == instance.Id).OrderBy(a => a.StartedAt).ToListAsync(ct);
            var activityDtos = new List<WorkspaceActivity>();
            var running = await WorkflowTreeGuard.CanRunAsync(db, instance.Id, ct);
            foreach (var activity in activityRows)
            {
                var definition = version!.Activities.First(a => a.NodeKey == activity.ActivityNodeKey);
                var config = IntegrationJson.Read<BusinessActivityConfiguration>(definition.ConfigurationJson);
                var items = await db.WorkItems.Where(w => w.ActivityInstanceId == activity.Id).ToListAsync(ct);
                var tasks = new List<WorkspaceTask>();
                foreach (var item in items)
                {
                    var member = await groups.IsMemberAsync(instance.OrganizationId, item.AssignmentGroupId, effective, ct);
                    var canAct = running && member && (item.Status == WorkItemStatus.Pending || item.Status == WorkItemStatus.Claimed && item.ClaimedByUserId == effective);
                    tasks.Add(new(await assembler.ToDtoAsync(item, true, ct), canAct, canAct ? null : !running ? "Workflow or parent is paused or finished." : !member ? "Assigned to another group." : "Task is completed or claimed by another user."));
                }
                activityDtos.Add(new(activity.Id, activity.ActivityNodeKey, activity.Name, activity.ActivityType.ToString(), activity.Status.ToString(), activity.Phase,
                    activity.StartedAt, activity.CompletedAt, activity.DueAt, config.DepartmentCode, config.FieldActivityCode, tasks,
                    definition.AssignmentRules.FirstOrDefault(r => r.IsActive)?.ReferenceId is Guid assigned ? groupNames.GetValueOrDefault(assigned) : null));
            }
            tree.Add(new(instance.Id, instance.ParentInstanceId, instance.ParentActivityInstanceId, name, instance.Status.ToString(), activityDtos));
        }
        var ids = all.Select(i => i.Id).ToArray();
        var audit = await db.WorkflowEvents.AsNoTracking().Where(e => ids.Contains(e.WorkflowInstanceId)).OrderByDescending(e => e.OccurredAt).Take(1000).ToListAsync(ct);
        var actors = await db.Participants.AsNoTracking().ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);
        var history = audit.OrderBy(e => e.OccurredAt).Select(e => new WorkspaceHistory(e.Id, e.WorkflowInstanceId, e.EventType.ToString(), e.ActivityNodeKey, e.ActorUserId,
            e.ActorUserId is Guid user ? actors.GetValueOrDefault(user) : null, e.OccurredAt, e.PayloadJson)).ToList();
        var operations = await db.IntegrationJobs.AsNoTracking().Where(j => ids.Contains(j.WorkflowInstanceId)).OrderByDescending(j => j.CreatedAt).Take(200)
            .Select(j => new WorkspaceOperation(j.Id, j.WorkflowInstanceId, j.ActivityInstanceId, j.EventName, j.Kind, j.EventTrigger, j.Required, j.Status, j.Attempts, j.Error, j.StatusCode, j.DeliveryResultJson)).ToListAsync(ct);
        var demoActors = administrator && root.IsDemo ? await db.Participants.Where(p => p.IsActive && p.IsDemo).Select(p => new WorkspaceActor(p.UserId, p.DisplayName)).ToListAsync(ct) : [];
        return Result.Success(new WorkspaceDetail(root.Id, root.IsDemo, root.GeographyJson, tree, history, operations, demoActors));
    }

    public async Task<Result> ActAsync(Guid workItemId, WorkspaceActionInput input, Guid actor, bool administrator, CancellationToken ct)
    {
        var instanceId = await db.WorkItems.Where(w => w.Id == workItemId).Select(w => (Guid?)w.WorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is null) return Result.Failure(Invalid("Task not found."));
        return await WorkflowExecutionLock.RunAsync(db, "instance:" + instanceId, async () =>
        {
            var item = await db.WorkItems.FindAsync([workItemId], ct);
            var instance = await db.WorkflowInstances.FindAsync([instanceId.Value], ct);
            if (item is null || instance is null || input.RequestId == Guid.Empty || actor == Guid.Empty) return Result.Failure(Invalid("A task, signed-in actor and request identifier are required."));
            if (!await ValidDemoActorAsync(instance, input.DemoActorId, administrator, ct)) return Result.Failure(Invalid("Demo actor selection is not permitted."));
            var effective = input.DemoActorId ?? actor;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workItemId, input, actor }))));
            var receipt = await db.WorkspaceActions.FirstOrDefaultAsync(r => r.RequestId == input.RequestId, ct);
            if (receipt is not null) return receipt.Fingerprint == fingerprint ? Result.Success() : Result.Failure(Invalid("This request identifier was already used for a different action."));
            if (!await WorkflowTreeGuard.CanRunAsync(db, instance.Id, ct)) return Result.Failure(Invalid("This workflow or its parent is not running."));
            if (!await groups.IsMemberAsync(tenant.OrganizationId, item.AssignmentGroupId, effective, ct)) return Result.Failure(Invalid("The selected user does not belong to the assigned group."));
            if (item.Status is not (WorkItemStatus.Pending or WorkItemStatus.Claimed)) return Result.Failure(Invalid("This task is already finished."));
            var execution = await db.ActivityInstances.FindAsync([item.ActivityInstanceId], ct);
            var version = await versions.GetByIdWithProjectionAsync(instance.PinnedWorkflowVersionId, ct);
            var definition = version!.Activities.First(a => a.NodeKey == execution!.ActivityNodeKey);
            if (definition.ActivityType == ActivityType.MainActivity && execution!.Phase != "AwaitingApproval") return Result.Failure(Invalid("Complete the child workflow before acting on its parent."));
            if (input.Comment?.Length > 4000) return Result.Failure(Invalid("Comments cannot exceed 4000 characters."));
            if ((input.Action.Equals("comment", StringComparison.OrdinalIgnoreCase) || input.Action.Equals("reject", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrWhiteSpace(input.Comment)) return Result.Failure(Invalid("A comment is required."));
            if (input.Action == "comment")
            {
                db.WorkflowEvents.Add(WorkflowEvent.Append(instance.OrganizationId, instance.Id, WorkflowEventType.CommentAdded, DateTime.UtcNow, execution!.ActivityNodeKey, effective,
                    JsonSerializer.Serialize(new { comment = input.Comment, administratorId = input.DemoActorId is null ? (Guid?)null : actor, input.RequestId })));
                var queued = await events.QueueAsync(instance, definition, execution, "OnComment", input.RequestId.ToString("N"), ct,
                    new() { ["comment"] = input.Comment, ["ActorUserId"] = effective.ToString() });
                if (queued.IsFailure) return Result.Failure(queued.Error);
            }
            else
            {
                if (!definition.Outcomes.Any(o => o.IsActive && o.OutcomeKey == input.Action)) return Result.Failure(Invalid("Select a configured task outcome."));
                if (item.Status == WorkItemStatus.Pending)
                { var claimed = await sender.Send(new ClaimWorkItemCommand(item.Id, effective, tenant.OrganizationId), ct); if (claimed.IsFailure) return Result.Failure(claimed.Error); }
                var completed = await sender.Send(new CompleteWorkItemCommand(item.Id, effective, tenant.OrganizationId, input.Action, input.Comment, FormValues: input.FormValues), ct);
                if (completed.IsFailure) return Result.Failure(completed.Error);
            }
            db.WorkspaceActions.Add(WorkflowWorkspaceAction.Create(tenant.OrganizationId, input.RequestId, workItemId, actor, effective, fingerprint));
            if (input.DemoActorId is not null) db.WorkflowEvents.Add(WorkflowEvent.Append(instance.OrganizationId, instance.Id, WorkflowEventType.DemoAction, DateTime.UtcNow,
                execution!.ActivityNodeKey, actor, JsonSerializer.Serialize(new { effectiveActorId = effective, action = input.Action, input.RequestId })));
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }, ct);
    }
}
