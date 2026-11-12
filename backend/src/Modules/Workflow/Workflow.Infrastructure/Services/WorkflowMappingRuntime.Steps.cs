using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

namespace Workflow.Infrastructure.Services;

internal sealed partial class WorkflowMappingRuntime
{
    public async Task PrepareStepAsync(WorkItem item, string configurationJson, CancellationToken ct)
    {
        var business = IntegrationJson.Read<BusinessActivityConfiguration>(configurationJson);
        if (business.TypedMapping is null || business.TypedMapping.StepBindings.Count == 0) return;
        if (business.FormSteps.Count == 0) throw new FormatException("Typed Step bindings require pinned form steps.");
        var progress = ActivityFormProgress.Read(item.FormProgressJson, business.FormSteps, item.Id);
        if (progress.ExitNodeKey is not null) return;
        var mapping = business.TypedMapping with { StepBindings = business.TypedMapping.StepBindings.Where(b => b.FormStepId == progress.ActiveStepId).ToList() };
        var invocation = await db.MappingInvocations.SingleOrDefaultAsync(i => i.OriginActivityId == item.ActivityInstanceId && i.FormVisitId == progress.VisitId, ct);
        if (invocation is null)
        {
            var instance = await db.WorkflowInstances.FindAsync([item.WorkflowInstanceId], ct) ?? throw new InvalidOperationException("Missing Step instance.");
            var root = await WorkflowTreeGuard.RootAsync(db, instance.Id, ct);
            var rootInstance = await db.WorkflowInstances.FindAsync([root], ct) ?? instance;
            var activity = await db.ActivityInstances.FindAsync([item.ActivityInstanceId], ct) ?? throw new InvalidOperationException("Missing Step activation.");
            business.TypedMapping = mapping;
            invocation = WorkflowMappingInvocation.Create(item.OrganizationId, root, activity.Id, activity.ExecutionTokenId,
                rootInstance.StartedByUserId ?? throw new UnauthorizedAccessException("Step mapping requires a persisted actor."), instance.Id,
                instance.PinnedWorkflowVersionId, activity.ActivityNodeKey, "Step", Json(business), item.Id + ":step:" + progress.VisitId,
                DateTime.UtcNow, formVisitId: progress.VisitId);
            db.MappingInvocations.Add(invocation); item.SetFormProgress(JsonSerializer.Serialize(progress));
            await db.SaveChangesAsync(ct);
        }
        item.WaitForMapping(); await PrepareAsync(invocation, ct);
        if (invocation.Applied) item.MappingReady();
        await db.SaveChangesAsync(ct);
    }

    public async Task<StepMappingSnapshot?> ReadStepAsync(WorkItem item, Guid visitId, CancellationToken ct)
    {
        var invocation = await db.MappingInvocations.AsNoTracking().SingleOrDefaultAsync(i => i.OriginActivityId == item.ActivityInstanceId && i.FormVisitId == visitId && i.ExecutionKind == "Step", ct);
        if (invocation is null) return null;
        return StepSnapshot(invocation);
    }
    internal static StepMappingSnapshot StepSnapshot(WorkflowMappingInvocation invocation)
    {
        var values = Read<Dictionary<string, JsonElement>>(invocation.StepValuesJson ?? "{}");
        return new(invocation.Id, invocation.FormVisitId!.Value, invocation.Status, Config(invocation).StepBindings.Where(b => values.ContainsKey(b.Id))
            .Select(b => new StepMappingValue(b.Id, b.FormStepId, b.FormId, b.FormVersion, b.FieldKey, b.Mode, values[b.Id])).ToList(),
            Read<List<ResolvedMappingSource>>(invocation.SourcesJson).Select(s => s with { Value = default }).ToList());
    }
}

internal sealed class WorkflowFormSubmissionGuard(WorkflowDbContext db) : IFormSubmissionGuard
{
    public async Task<Result> ValidateAsync(Guid formId, int? version, string? contextType, string? contextId,
        Guid? clientSubmissionId, IReadOnlyDictionary<string, object?> answers, string? actorId, CancellationToken ct)
    {
        Result Denied() => Result.Failure(new Error("Workflow.Form.Context", "The workflow form context, activation or readonly values are invalid."));
        // Form submission identifiers are global across tenants. Reserve workflow receipts globally,
        // but never disclose or load another tenant's payload while checking the reservation.
        if (!string.Equals(contextType, "WorkItem", StringComparison.OrdinalIgnoreCase))
        {
            var reserved = clientSubmissionId is Guid key && await db.FormReceiptReservations.IgnoreQueryFilters().AnyAsync(r => r.ReceiptKey == key, ct);
            return reserved ? Denied() : Result.Success();
        }
        var pieces = contextId?.Split(':') ?? [];
        if (pieces.Length is < 1 or > 2 || !Guid.TryParse(pieces[0], out var itemId)) return Denied();
        var item = await db.WorkItems.FindAsync([itemId], ct);
        if (item is null) return Denied();
        var instance = await db.WorkflowInstances.FindAsync([item.WorkflowInstanceId], ct);
        var activity = await db.ActivityInstances.FindAsync([item.ActivityInstanceId], ct);
        if (instance is null || activity is null || !Guid.TryParse(actorId, out var actor)) return Denied();
        var node = await db.ActivityDefinitions.SingleOrDefaultAsync(a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId && a.NodeKey == activity.ActivityNodeKey, ct);
        if (node is null) return Denied();
        var config = IntegrationJson.Read<BusinessActivityConfiguration>(node?.ConfigurationJson);
        Guid visitId; var replay = false;
        if (config.FormSteps.Count > 0)
        {
            var progress = ActivityFormProgress.Read(item.FormProgressJson, config.FormSteps, item.Id);
            if (pieces.Length != 2 || !Guid.TryParse(pieces[1], out visitId)) return Denied();
            var completed = progress.Visits.SingleOrDefault(v => v.VisitId == visitId);
            if (completed is not null)
            {
                if (completed.ActorId != actor || completed.FormId != formId || completed.VersionNo != version || completed.SubmissionId == Guid.Empty || clientSubmissionId != (completed.ReceiptKey ?? completed.VisitId)) return Denied();
                replay = true;
            }
            else
            {
                var step = config.FormSteps.Single(s => s.Id == progress.ActiveStepId);
                if (visitId != progress.VisitId || progress.ExitNodeKey is not null || step.FormId != formId || step.VersionNo != version || clientSubmissionId != item.FormReceiptKey(visitId, actor)) return Denied();
            }
        }
        else
        {
            visitId = item.Id;
            if (pieces.Length != 1 || config.FormDefinitionId != formId || config.FormVersionNo != version) return Denied();
            replay = item.Status == WorkItemStatus.Completed && item.CompletedByUserId == actor;
            if (clientSubmissionId != (replay ? item.CompletedFormReceiptKey ?? item.Id : item.FormReceiptKey(visitId, actor))) return Denied();
        }
        if (replay)
        {
            if (item.Status is not (WorkItemStatus.Claimed or WorkItemStatus.Completed) || !await WorkflowTreeGuard.CanRunAsync(db, item.WorkflowInstanceId, ct, allowCompleted: true)) return Denied();
        }
        else if (item.Status != WorkItemStatus.Claimed || item.ClaimedByUserId != actor || !await WorkflowTreeGuard.CanRunAsync(db, item.WorkflowInstanceId, ct)) return Denied();
        if (config.TypedMapping is null || config.TypedMapping.StepBindings.Count == 0) return Result.Success();
        var invocation = await db.MappingInvocations.AsNoTracking().SingleOrDefaultAsync(i => i.OriginActivityId == item.ActivityInstanceId && i.FormVisitId == visitId && i.ExecutionKind == "Step", ct);
        var snapshot = invocation is null ? null : WorkflowMappingRuntime.StepSnapshot(invocation);
        if (snapshot?.Status != "Completed") return Denied();
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in answers)
            if (!normalized.TryAdd(answer.Key.Trim(), answer.Value)) return Denied();
        foreach (var field in snapshot.Values.Where(v => v.Mode == "ReadOnly"))
        {
            if (!normalized.TryGetValue(field.FieldKey.Trim(), out var actual)
                || !JsonElement.DeepEquals(field.Value, JsonSerializer.SerializeToElement(actual))) return Denied();
        }
        return Result.Success();
    }
}
