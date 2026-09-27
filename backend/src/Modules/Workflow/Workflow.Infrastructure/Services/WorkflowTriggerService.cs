namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;

/// <summary>
/// Implements IWorkflowTriggerService — the cross-module contract used by other modules
/// (e.g. Consent) to fire workflow triggers without depending on Workflow internals.
/// Shadow mode constraint: this service never modifies the calling module's data.
/// ExecutionPolicy controls start vs signal vs skip when a matching instance already exists.
/// </summary>
internal sealed class WorkflowTriggerService : IWorkflowTriggerService
{
    private readonly IWorkflowBindingResolver _bindingResolver;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IWorkflowRuntimeEngine _engine;
    private readonly IWorkflowInboxWriter _inboxWriter;
    private readonly IWorkflowIntegrationInboxRepository _inboxRepo;
    private readonly ILogger<WorkflowTriggerService> _logger;
    private readonly WorkflowDbContext? _db;

    public WorkflowTriggerService(
        IWorkflowBindingResolver bindingResolver,
        IWorkflowInstanceRepository instanceRepo,
        IWorkflowRuntimeEngine engine,
        IWorkflowInboxWriter inboxWriter,
        IWorkflowIntegrationInboxRepository inboxRepo,
        ILogger<WorkflowTriggerService> logger, WorkflowDbContext? db = null)
    {
        _bindingResolver = bindingResolver;
        _instanceRepo    = instanceRepo;
        _engine          = engine;
        _inboxWriter     = inboxWriter;
        _inboxRepo       = inboxRepo;
        _logger          = logger;
        _db              = db;
    }

    public async Task TriggerAsync(
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        string businessEntityId,
        string idempotencyKey,
        string? correlationId = null,
        IReadOnlyDictionary<string, object?>? payload = null,
        CancellationToken cancellationToken = default)
    {
        var messageId = idempotencyKey;
        var existingInbox = await _inboxWriter.GetByMessageIdAsync(messageId, cancellationToken);
        if (existingInbox is not null)
        {
            _logger.LogDebug(
                "Inbox message {MessageId} already exists for org {OrgId}. Skipping duplicate trigger.",
                messageId, organizationId);
            return;
        }

        var bindingResult = await _bindingResolver.ResolveAsync(
            organizationId, moduleKey, entityType, triggerEvent, cancellationToken);
        if (bindingResult.IsFailure)
        {
            _logger.LogDebug(
                "No active workflow binding for {Module}/{Entity}/{Trigger} in org {OrgId}. Skipping.",
                moduleKey, entityType, triggerEvent, organizationId);
            return;
        }

        var binding = bindingResult.Value;
        var now = DateTime.UtcNow;
        var payloadJson = payload is null
            ? "{}"
            : JsonSerializer.Serialize(payload);

        if (!EvaluateStartCondition(binding, payload))
        {
            _logger.LogDebug(
                "Start condition failed for binding {BindingId} ({Module}/{Entity}/{Trigger}). Skipping start.",
                binding.Id, moduleKey, entityType, triggerEvent);
            return;
        }

        var existingInstance = await _instanceRepo.GetByBusinessEntityAsync(
            organizationId, moduleKey, entityType, businessEntityId, cancellationToken);

        var decision = DecideTriggerAction(binding.ExecutionPolicy, existingInstance);
        if (decision == TriggerAction.Skip)
        {
            _logger.LogDebug(
                "ExecutionPolicy {Policy} skipped trigger for {Module}/{Entity}/{Trigger} entity {EntityId} (existing status {Status}).",
                binding.ExecutionPolicy, moduleKey, entityType, triggerEvent, businessEntityId,
                existingInstance?.Status);
            return;
        }

        if (decision == TriggerAction.Signal)
        {
            await SignalExistingAsync(
                organizationId, moduleKey, entityType, triggerEvent, businessEntityId,
                messageId, idempotencyKey, correlationId, payloadJson, binding, existingInstance!, now,
                cancellationToken);
            return;
        }

        var alreadyStarted = await _instanceRepo.ExistsByIdempotencyKeyAsync(
            organizationId, idempotencyKey, cancellationToken);
        if (alreadyStarted)
        {
            _logger.LogDebug(
                "Workflow instance for idempotency key {Key} already exists in org {OrgId}. Skipping duplicate trigger.",
                idempotencyKey, organizationId);
            return;
        }

        var inbox = await _inboxWriter.WritePendingAsync(
            organizationId,
            messageId,
            idempotencyKey,
            moduleKey,
            entityType,
            businessEntityId,
            triggerEvent,
            payloadJson,
            now,
            correlationId,
            binding.Id,
            cancellationToken);

        inbox.MarkProcessing(now);

        try
        {
            var result = await _engine.StartAsync(
                organizationId,
                binding.Id,
                businessEntityId,
                idempotencyKey,
                DateTime.UtcNow,
                correlationId,
                startedByUserId: TryGetStartedByUserId(payload),
                cancellationToken: cancellationToken);

            if (result.IsFailure)
            {
                inbox.MarkFailed(result.Error.Message, DateTime.UtcNow);
                await _inboxRepo.SaveChangesAsync(cancellationToken);
                _logger.LogError(
                    "Failed to start workflow instance for {Module}/{Entity}/{Trigger} in org {OrgId}: {Error}",
                    moduleKey, entityType, triggerEvent, organizationId, result.Error.Message);
                return;
            }

            inbox.MarkProcessed(result.Value.Id, DateTime.UtcNow);
            await _inboxRepo.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            inbox.MarkFailed(ex.Message, DateTime.UtcNow);
            await _inboxRepo.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex,
                "Exception starting workflow for {Module}/{Entity}/{Trigger} in org {OrgId}",
                moduleKey, entityType, triggerEvent, organizationId);
        }
    }

    private async Task SignalExistingAsync(
        Guid organizationId,
        string moduleKey,
        string entityType,
        string triggerEvent,
        string businessEntityId,
        string messageId,
        string idempotencyKey,
        string? correlationId,
        string payloadJson,
        WorkflowBinding binding,
        WorkflowInstance existingInstance,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (_db is not null)
        {
            await WorkflowExecutionLock.RunAsync(_db, "instance:" + existingInstance.Id, async () => { await SignalCore(); return true; }, cancellationToken);
        }
        else await SignalCore();

        async Task SignalCore()
        {
        var inbox = await _inboxWriter.WritePendingAsync(
            organizationId,
            messageId,
            idempotencyKey,
            moduleKey,
            entityType,
            businessEntityId,
            triggerEvent,
            payloadJson,
            now,
            correlationId,
            binding.Id,
            cancellationToken);

        Guid? targetActivity = null;
        if (_db is not null)
        {
            var waits = await _db.ActivityInstances.Where(a => a.WorkflowInstanceId == existingInstance.Id
                && a.ActivityType == ActivityType.WaitEvent && a.Status == ActivityInstanceStatus.Active).ToListAsync(cancellationToken);
            var definitions = await _db.ActivityDefinitions.Where(a => a.WorkflowVersionId == existingInstance.PinnedWorkflowVersionId).ToListAsync(cancellationToken);
            targetActivity = waits.FirstOrDefault(a => definitions.Any(d => d.NodeKey == a.ActivityNodeKey && MatchesEvent(d.ConfigurationJson, triggerEvent)))?.Id;
        }
        inbox.SetSignalTarget(existingInstance.Id, targetActivity);
        inbox.MarkProcessing(now);
        await _inboxRepo.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _engine.ResumeFromExternalSignalAsync(
                existingInstance.Id,
                triggerEvent,
                DateTime.UtcNow,
                cancellationToken);

            if (result.IsFailure)
            {
                inbox.MarkFailed(result.Error.Message, DateTime.UtcNow);
                await _inboxRepo.SaveChangesAsync(cancellationToken);
                _logger.LogError(
                    "Failed to signal instance {InstanceId} for {Module}/{Entity}/{Trigger} in org {OrgId}: {Error}",
                    existingInstance.Id, moduleKey, entityType, triggerEvent, organizationId, result.Error.Message);
                return;
            }

            inbox.MarkProcessed(existingInstance.Id, DateTime.UtcNow);
            await _inboxRepo.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            inbox.MarkFailed(ex.Message, DateTime.UtcNow);
            await _inboxRepo.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex,
                "Exception signaling instance {InstanceId} for {Module}/{Entity}/{Trigger} in org {OrgId}",
                existingInstance.Id, moduleKey, entityType, triggerEvent, organizationId);
        }
        }
    }

    private static bool MatchesEvent(string? json, string key)
    {
        try { using var document = JsonDocument.Parse(json ?? "{}"); var root = document.RootElement;
            return (root.TryGetProperty("eventKey", out var value) || root.TryGetProperty("signalKey", out value))
                && value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), key, StringComparison.OrdinalIgnoreCase); }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Decides whether a trigger should start a new instance, signal an existing one, or skip.
    /// </summary>
    internal static TriggerAction DecideTriggerAction(
        WorkflowExecutionPolicy policy,
        WorkflowInstance? existing)
    {
        var active = existing is not null && IsActive(existing.Status);
        var terminal = existing is not null && IsTerminal(existing.Status);

        return policy switch
        {
            WorkflowExecutionPolicy.SignalExistingInstance =>
                active ? TriggerAction.Signal : TriggerAction.Skip,

            WorkflowExecutionPolicy.StartIfNoRunningInstance =>
                active ? TriggerAction.Skip : TriggerAction.Start,

            WorkflowExecutionPolicy.RestartAfterTerminal =>
                active ? TriggerAction.Skip
                : existing is null || terminal ? TriggerAction.Start
                : TriggerAction.Skip,

            _ => TriggerAction.Start,
        };
    }

    private static bool IsActive(WorkflowInstanceStatus status) =>
        status is WorkflowInstanceStatus.Pending
            or WorkflowInstanceStatus.Running
            or WorkflowInstanceStatus.Suspended;

    private static bool IsTerminal(WorkflowInstanceStatus status) =>
        status is WorkflowInstanceStatus.Completed
            or WorkflowInstanceStatus.Cancelled
            or WorkflowInstanceStatus.Failed;

    internal enum TriggerAction
    {
        Start,
        Signal,
        Skip,
    }

    /// <summary>
    /// Reads the initiating user from common payload keys so Active-mode outcome
    /// callbacks (e.g. Consent approve/reject) receive a usable ActorUserId.
    /// </summary>
    internal static Guid? TryGetStartedByUserId(IReadOnlyDictionary<string, object?>? payload)
    {
        if (payload is null) return null;

        foreach (var key in new[]
                 {
                     "StartedByUserId", "RequesterUserId", "ActorUserId",
                     "CreatedBy", "CreatedByUserId", "UserId"
                 })
        {
            if (!payload.TryGetValue(key, out var raw) || raw is null)
                continue;

            if (raw is Guid g && g != Guid.Empty)
                return g;

            if (Guid.TryParse(raw.ToString(), out var parsed) && parsed != Guid.Empty)
                return parsed;
        }

        return null;
    }

    /// <summary>
    /// Evaluates binding.StartConditionExpression or ConditionJson using simple "field == value" rules.
    /// Returns true when no condition is set or the condition matches.
    /// </summary>
    internal static bool EvaluateStartCondition(
        WorkflowBinding binding,
        IReadOnlyDictionary<string, object?>? payload)
    {
        var expression = binding.StartConditionExpression;
        if (string.IsNullOrWhiteSpace(expression) && !string.IsNullOrWhiteSpace(binding.ConditionJson))
        {
            expression = TryExtractExpressionFromConditionJson(binding.ConditionJson);
        }

        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var vars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (payload is not null)
        {
            foreach (var (key, value) in payload)
                vars[key] = value?.ToString();
        }

        return EvaluateSimpleEquality(expression, vars);
    }

    private static string? TryExtractExpressionFromConditionJson(string conditionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(conditionJson);
            if (doc.RootElement.TryGetProperty("expression", out var expr))
                return expr.GetString();
            if (doc.RootElement.TryGetProperty("Expression", out var expr2))
                return expr2.GetString();
        }
        catch (JsonException)
        {
            // ignore malformed JSON — treat as no condition
        }

        return null;
    }

    private static bool EvaluateSimpleEquality(string expression, IReadOnlyDictionary<string, string?> vars)
    {
        var parts = expression.Split("==", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return true;

        var field = parts[0].Trim().Trim('\'', '"');
        var expected = parts[1].Trim().Trim('\'', '"');
        if (!vars.TryGetValue(field, out var actual))
            return false;

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
