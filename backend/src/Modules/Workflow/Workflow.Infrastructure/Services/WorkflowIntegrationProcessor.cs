namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workflow.Application.Abstractions;
using Workflow.Application.Integrations;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;

internal sealed class WorkflowIntegrationProcessor(WorkflowDbContext db, WorkflowIntegrations integrations,
    WorkflowIntegrationTransport transport, IWorkflowRuntimeEngine engine, WorkflowActivityEvents? activityEvents = null)
{
    public async Task ProcessJobAsync(Guid id, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var claimed = await WorkflowExecutionLock.RunAsync(db, "job:" + id, async () =>
        {
            var job = await db.IntegrationJobs.FindAsync([id], ct);
            if (job is null) return false;
            var instance = await db.WorkflowInstances.FindAsync([job.WorkflowInstanceId], ct);
            if (instance?.Status == WorkflowInstanceStatus.Cancelled || instance?.Status == WorkflowInstanceStatus.Completed && !(job.IsActivityEvent && !job.Required))
            { job.Cancel(); return false; }
            if (instance is null || !await WorkflowTreeGuard.CanRunAsync(db, instance.Id, ct, job.IsActivityEvent && !job.Required)) return false;
            if (job.Status == "Delivered") return true;
            var now = DateTime.UtcNow;
            if (!(job.Status == "Pending" && job.NextAttemptAt <= now || job.Status == "Running" && job.LeaseUntil <= now)) return false;
            job.Claim(owner, now.AddMinutes(3)); await db.SaveChangesAsync(ct); return true;
        }, ct);
        if (!claimed) return;
        var operation = await db.IntegrationJobs.FindAsync([id], ct);
        if (operation is null) return;
        if (operation.Status != "Delivered")
        {
            var connection = await db.IntegrationConnections.FindAsync([operation.ConnectionId], ct);
            var http = IntegrationJson.Read<HttpActivityConfiguration>(operation.ConfigurationJson);
            var email = IntegrationJson.Read<EmailActivityConfiguration>(operation.ConfigurationJson);
            var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(operation.InputJson) ?? [];
            var result = connection is null ? new IntegrationResult(false, null, "", "Connection is unavailable.")
                : operation.Kind == "Http"
                    ? await transport.SendHttpAsync(connection, integrations.Credentials(connection), http, variables, operation.OperationKey, ct)
                    : await transport.SendEmailAsync(connection, integrations.Credentials(connection), email, variables, operation.OperationKey, ct);
            await WorkflowExecutionLock.RunAsync(db, "job:" + id, async () =>
            {
                await db.Entry(operation).ReloadAsync(ct);
                if (operation.LeaseOwner != owner || operation.Status != "Running") return false;
                var maxAttempts = operation.Kind == "Http" ? http.MaxAttempts : email.FailurePolicy == "Retry" ? email.MaxAttempts : 1;
                var delay = operation.Kind == "Http" ? http.RetryDelaySeconds : email.RetryDelaySeconds;
                if (!result.Success && result.Retryable && operation.Attempts < Math.Clamp(maxAttempts, 1, 10))
                    operation.Fail(result.Error ?? "Integration failed.", result.StatusCode, DateTime.UtcNow.AddSeconds(Math.Clamp(delay, 1, 3600) * Math.Min(operation.Attempts, 10)));
                else operation.RecordDelivery(JsonSerializer.Serialize(result, IntegrationJson.Options), result.StatusCode);
                await db.SaveChangesAsync(ct); return true;
            }, ct);
        }
        if (operation.Status != "Delivered") return;
        await WorkflowExecutionLock.RunAsync(db, "instance:" + operation.WorkflowInstanceId, async () =>
        {
            await db.Entry(operation).ReloadAsync(ct);
            var instance = await db.WorkflowInstances.FindAsync([operation.WorkflowInstanceId], ct);
            if (instance is not null) await db.Entry(instance).ReloadAsync(ct);
            if (instance?.Status == WorkflowInstanceStatus.Cancelled || instance?.Status == WorkflowInstanceStatus.Completed && !(operation.IsActivityEvent && !operation.Required)) { operation.Cancel(); return false; }
            if (instance is null || !await WorkflowTreeGuard.CanRunAsync(db, instance.Id, ct, operation.IsActivityEvent && !operation.Required) || operation.Status != "Delivered") return false;
            var result = JsonSerializer.Deserialize<IntegrationResult>(operation.ResultJson!, IntegrationJson.Options)!;
            var http = IntegrationJson.Read<HttpActivityConfiguration>(operation.ConfigurationJson);
            var email = IntegrationJson.Read<EmailActivityConfiguration>(operation.ConfigurationJson);
            var outcome = result.Success ? "success" : result.TimedOut ? http.TimeoutOutcome : http.ErrorOutcome;
            var error = result.Error;
            var outputs = new Dictionary<string, object?>();
            if (result.Success && operation.Kind == "Http")
            {
                try
                {
                    object? body;
                    try { body = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(result.Body) ? "null" : result.Body); }
                    catch (JsonException) { body = result.Body; }
                    var envelope = JsonSerializer.Serialize(new { status = result.StatusCode, headers = result.Headers, body });
                    outputs = IntegrationValueMapper.Map(envelope, http.OutputMappings);
                    if (http.Protocol == "Soap") foreach (var (key, value) in SoapMessage.Map(result.Body, http)) outputs[key] = value;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Xml.XmlException or System.Xml.XPath.XPathException)
                { error = ex.Message; outcome = http.ErrorOutcome; result = result with { Success = false, Error = error }; }
            }
            if (operation.Kind == "Email" && !result.Success && email.FailurePolicy == "Continue") { error = null; outcome = "success"; }
            if (operation.IsActivityEvent)
            {
                if (result.Success)
                {
                    if (operation.Required)
                    {
                        foreach (var (key, value) in outputs)
                        {
                            var variable = await db.WorkflowVariables.FirstOrDefaultAsync(v => v.WorkflowInstanceId == instance.Id && v.VariableName == key, ct);
                            if (variable is null) db.WorkflowVariables.Add(WorkflowVariable.Create(instance.OrganizationId, instance.Id, key, VariableDataType.Json, JsonSerializer.Serialize(value), DateTime.UtcNow));
                            else variable.SetValue(JsonSerializer.Serialize(value), DateTime.UtcNow);
                        }
                    }
                    operation.Complete(JsonSerializer.Serialize(outputs), result.StatusCode);
                }
                else
                {
                    operation.Fail(result.Error ?? "Integration failed.", result.StatusCode, null);
                    if (activityEvents is not null && operation.EventTrigger != "OnFailure")
                    {
                        var execution = await db.ActivityInstances.FindAsync([operation.ActivityInstanceId], ct);
                        var definition = await db.ActivityDefinitions.FirstAsync(a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId && a.NodeKey == execution!.ActivityNodeKey, ct);
                        await activityEvents.QueueAsync(instance, definition, execution!, "OnFailure", operation.Id.ToString("N"), ct);
                    }
                }
                db.WorkflowEvents.Add(WorkflowEvent.Append(instance.OrganizationId, instance.Id,
                    result.Success ? WorkflowEventType.IntegrationCompleted : WorkflowEventType.IntegrationFailed, DateTime.UtcNow,
                    payloadJson: JsonSerializer.Serialize(new { operationId = operation.Id, operation.EventName, operation.Required, operation.Attempts, result.Error })));
                await db.SaveChangesAsync(ct);
                return true;
            }
            var completion = await engine.CompleteExternalActivityAsync(operation.ActivityInstanceId, outputs, outcome, error, DateTime.UtcNow, ct);
            if (completion.IsFailure) return false;
            if (result.Success) operation.Complete(JsonSerializer.Serialize(outputs), result.StatusCode);
            else operation.Fail(result.Error ?? "Integration failed.", result.StatusCode, null);
            if (operation.Kind == "Email")
            {
                var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(operation.InputJson) ?? [];
                var recipients = IntegrationValueMapper.Render(string.Join(",", email.To, email.Cc, email.Bcc), variables);
                foreach (var address in WorkflowIntegrationTransport.SplitAddresses(recipients).Distinct(StringComparer.OrdinalIgnoreCase))
                    db.WorkflowNotificationLogs.Add(WorkflowNotificationLog.Create(operation.OrganizationId, email.TemplateKey, "Email",
                        JsonSerializer.Serialize(new[] { address }), "{}", DateTime.UtcNow, operation.OperationKey,
                        result.Success ? WorkflowNotificationLogStatus.Delivered : WorkflowNotificationLogStatus.Failed));
            }
            await db.SaveChangesAsync(ct); return true;
        }, ct);
    }

    public async Task ProcessEventsAsync(CancellationToken ct)
    {
        // One short transaction serializes receipt matching and timeout selection;
        // no network operation runs inside this transaction.
        await WorkflowExecutionLock.RunAsync(db, "event-matching", async () =>
        {
            var receipts = await db.EventReceipts.Where(r => r.Status == "Received")
                .OrderByDescending(r => db.EventSubscriptions.Any(s => s.Status == "Waiting" && s.ConnectionId == r.ConnectionId && s.EventKey == r.EventKey && s.CorrelationId == r.CorrelationId))
                .ThenBy(r => r.CreatedAt).Take(100).ToListAsync(ct);
            foreach (var receipt in receipts)
            {
                var matches = await db.EventSubscriptions.Where(s => s.Status == "Waiting" && s.ConnectionId == receipt.ConnectionId
                    && s.EventKey == receipt.EventKey && s.CorrelationId == receipt.CorrelationId && s.ExpiresAt >= receipt.CreatedAt
                    && (receipt.ActivityInstanceId == null || s.ActivityInstanceId == receipt.ActivityInstanceId)).ToListAsync(ct);
                matches = matches.Where(s => s.Status == "Waiting").ToList();
                if (matches.Count > 1) { receipt.Finish("Ambiguous", error: "More than one active wait matches this event."); continue; }
                if (matches.Count == 0)
                {
                    if (await db.EventSubscriptions.AnyAsync(s => s.ConnectionId == receipt.ConnectionId && s.EventKey == receipt.EventKey && s.CorrelationId == receipt.CorrelationId
                        && (s.Status != "Waiting" || s.ExpiresAt < receipt.CreatedAt), ct)) receipt.Finish("Late", error: "The matching wait has already finished or its deadline passed.");
                    else if (receipt.CreatedAt < DateTime.UtcNow.AddDays(-7)) receipt.Finish("Expired", error: "No matching wait within seven days.");
                    continue;
                }
                var wait = matches[0];
                await WorkflowExecutionLock.RunAsync(db, "instance:" + wait.WorkflowInstanceId, async () =>
                {
                var instance = await db.WorkflowInstances.FindAsync([wait.WorkflowInstanceId], ct);
                if (instance is not null && db.Entry(instance).State == EntityState.Unchanged) await db.Entry(instance).ReloadAsync(ct);
                if (instance?.Status is WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Completed)
                { wait.Finish("Cancelled"); receipt.Finish("Rejected", error: "The workflow is terminal."); return false; }
                if (instance?.Status != WorkflowInstanceStatus.Running) return false;
                var config = IntegrationJson.Read<EventActivityConfiguration>(wait.ConfigurationJson);
                try
                {
                    var outputs = IntegrationValueMapper.Map(receipt.PayloadJson, config.OutputMappings);
                    var completion = await engine.CompleteExternalActivityAsync(wait.ActivityInstanceId, outputs, "received", null, DateTime.UtcNow, ct);
                    if (completion.IsSuccess) { wait.Finish("Received"); receipt.Finish("Processed", wait.ActivityInstanceId); }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                { receipt.Finish("Rejected", error: ex.Message); }
                return true;
                }, ct);
            }
            var now = DateTime.UtcNow;
            var expired = await db.EventSubscriptions.Where(s => s.Status == "Waiting" && s.ExpiresAt <= now).Take(100).ToListAsync(ct);
            foreach (var wait in expired)
            {
                await WorkflowExecutionLock.RunAsync(db, "instance:" + wait.WorkflowInstanceId, async () =>
                {
                var instance = await db.WorkflowInstances.FindAsync([wait.WorkflowInstanceId], ct);
                if (instance is not null && db.Entry(instance).State == EntityState.Unchanged) await db.Entry(instance).ReloadAsync(ct);
                if (instance?.Status is WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Completed) { wait.Finish("Cancelled"); return false; }
                if (instance?.Status != WorkflowInstanceStatus.Running) return false;
                // An on-time receipt already buffered while suspended takes precedence.
                if (await db.EventReceipts.AnyAsync(r => r.Status == "Received" && r.ConnectionId == wait.ConnectionId && r.EventKey == wait.EventKey
                    && r.CorrelationId == wait.CorrelationId && r.CreatedAt <= wait.ExpiresAt, ct)) return false;
                var config = IntegrationJson.Read<EventActivityConfiguration>(wait.ConfigurationJson);
                var completion = await engine.CompleteExternalActivityAsync(wait.ActivityInstanceId, new Dictionary<string, object?>(), config.TimeoutOutcome,
                    "The event wait timed out.", now, ct);
                if (completion.IsSuccess) wait.Finish("TimedOut");
                return true;
                }, ct);
            }
            await db.SaveChangesAsync(ct); return true;
        }, ct);
    }

    public async Task ProcessChildrenAsync(CancellationToken ct)
    {
        var children = await db.WorkflowInstances.AsNoTracking().Where(c => c.ParentInstanceId != null && c.ParentActivityNodeKey != null
            && (c.Status == WorkflowInstanceStatus.Completed || c.Status == WorkflowInstanceStatus.Failed || c.Status == WorkflowInstanceStatus.Cancelled)
            && db.ActivityInstances.Any(a => a.Id == c.ParentActivityInstanceId && a.Status == ActivityInstanceStatus.Active
                && (a.ActivityType == ActivityType.CallActivity || a.Phase == "WaitingForChild" || a.Phase == "ChildFailed" && c.Status == WorkflowInstanceStatus.Completed))
            && db.WorkflowInstances.Any(p => p.Id == c.ParentInstanceId && p.Status == WorkflowInstanceStatus.Running)).OrderBy(c => c.StartedAt).Take(50).ToListAsync(ct);
        foreach (var child in children)
            await engine.ResumeFromCallActivityAsync(child.ParentInstanceId!.Value, child.ParentActivityNodeKey!, DateTime.UtcNow, ct);
        var cancelledChildren = await db.WorkflowInstances.Where(c => c.ParentInstanceId != null
            && (c.Status == WorkflowInstanceStatus.Running || c.Status == WorkflowInstanceStatus.Suspended)
            && db.WorkflowInstances.Any(p => p.Id == c.ParentInstanceId && p.Status == WorkflowInstanceStatus.Cancelled)).Take(50).ToListAsync(ct);
        foreach (var child in cancelledChildren)
            await WorkflowExecutionLock.RunAsync(db, "instance:" + child.Id, async () =>
            {
                await db.Entry(child).ReloadAsync(ct);
                if (child.Status is not (WorkflowInstanceStatus.Running or WorkflowInstanceStatus.Suspended)) return false;
                var now = DateTime.UtcNow;
                child.Cancel(now);
                foreach (var item in await db.WorkItems.Where(w => w.WorkflowInstanceId == child.Id && (w.Status == WorkItemStatus.Pending || w.Status == WorkItemStatus.Claimed)).ToListAsync(ct)) item.Cancel(now);
                foreach (var timer in await db.WorkflowTimers.Where(t => t.WorkflowInstanceId == child.Id && (t.Status == WorkflowTimerStatus.Pending || t.Status == WorkflowTimerStatus.Fired)).ToListAsync(ct)) timer.Cancel(now);
                foreach (var token in await db.WorkflowExecutionTokens.Where(t => t.WorkflowInstanceId == child.Id && t.Status == ExecutionTokenStatus.Active).ToListAsync(ct)) token.Cancel(now);
                foreach (var wait in await db.EventSubscriptions.Where(w => w.WorkflowInstanceId == child.Id && w.Status == "Waiting").ToListAsync(ct)) wait.Finish("Cancelled");
                foreach (var job in await db.IntegrationJobs.Where(j => j.WorkflowInstanceId == child.Id && (j.Status == "Pending" || j.Status == "Running" || j.Status == "Delivered")).ToListAsync(ct)) job.Cancel();
                await db.SaveChangesAsync(ct); return true;
            }, ct);
    }
}
