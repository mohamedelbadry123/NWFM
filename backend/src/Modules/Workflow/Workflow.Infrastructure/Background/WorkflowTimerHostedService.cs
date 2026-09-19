namespace Workflow.Infrastructure.Background;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Application.Abstractions;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Persistence;

/// <summary>
/// Polls due workflow timers, overdue work-item reminders, and SLA escalations.
/// </summary>
internal sealed class WorkflowTimerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowTimerHostedService> _logger;

    public WorkflowTimerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowTimerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow timer hosted service started (interval={Interval}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueTimersAsync(stoppingToken);
                await ProcessOverdueRemindersAsync(stoppingToken);
                await ProcessEscalationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in workflow timer hosted service poll cycle");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessDueTimersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var timerRepo = scope.ServiceProvider.GetRequiredService<IWorkflowTimerRepository>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowRuntimeEngine>();

        var now = DateTime.UtcNow;
        var dueTimers = await timerRepo.GetPendingDueAsync(now, BatchSize, cancellationToken);

        foreach (var timer in dueTimers)
        {
            try
            {
                var result = await engine.ResumeFromTimerAsync(timer.Id, DateTime.UtcNow, cancellationToken);
                if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "ResumeFromTimer failed for timer {TimerId}: {Error}",
                        timer.Id, result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception resuming timer {TimerId}", timer.Id);
            }
        }
    }

    private async Task ProcessOverdueRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var workItemRepo = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var activityRepo = scope.ServiceProvider.GetRequiredService<IActivityInstanceRepository>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IWorkflowEventRepository>();
        var events = scope.ServiceProvider.GetRequiredService<IWorkflowEventAppender>();

        var now = DateTime.UtcNow;
        var overdue = await workItemRepo.GetOverdueForReminderAsync(now, BatchSize, cancellationToken);

        foreach (var item in overdue)
        {
            try
            {
                var instanceEvents = await eventRepo.GetByInstanceIdAsync(item.WorkflowInstanceId, cancellationToken);
                var activity = await activityRepo.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
                var nodeKey = activity?.ActivityNodeKey;

                var alreadyReminded = instanceEvents.Any(e =>
                    e.EventType == WorkflowEventType.ReminderSent
                    && (string.Equals(e.ActivityNodeKey, nodeKey, StringComparison.Ordinal)
                        || (e.PayloadJson is not null
                            && e.PayloadJson.Contains(item.Id.ToString(), StringComparison.OrdinalIgnoreCase))));

                if (alreadyReminded)
                    continue;

                await events.AppendAsync(
                    item.OrganizationId,
                    item.WorkflowInstanceId,
                    WorkflowEventType.ReminderSent,
                    now,
                    activityNodeKey: nodeKey,
                    payloadJson: $"{{\"workItemId\":\"{item.Id}\"}}",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending reminder for work item {WorkItemId}", item.Id);
            }
        }
    }

    private async Task ProcessEscalationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var workItemRepo = scope.ServiceProvider.GetRequiredService<IWorkItemRepository>();
        var activityRepo = scope.ServiceProvider.GetRequiredService<IActivityInstanceRepository>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IWorkflowEventRepository>();
        var events = scope.ServiceProvider.GetRequiredService<IWorkflowEventAppender>();
        var slaRepo = scope.ServiceProvider.GetRequiredService<ISlaPolicyRepository>();
        var assignmentResolver = scope.ServiceProvider.GetRequiredService<IWorkflowAssignmentResolver>();
        var candidateFactory = scope.ServiceProvider.GetRequiredService<IWorkflowCandidateFactory>();
        var candidateRepo = scope.ServiceProvider.GetRequiredService<IWorkItemCandidateRepository>();
        var instanceRepo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var now = DateTime.UtcNow;
        var overdue = await workItemRepo.GetOverdueForReminderAsync(now, BatchSize, cancellationToken);

        foreach (var item in overdue)
        {
            try
            {
                if (item.Status is WorkItemStatus.Completed or WorkItemStatus.Cancelled)
                    continue;

                var instanceEvents = await eventRepo.GetByInstanceIdAsync(item.WorkflowInstanceId, cancellationToken);
                var alreadyEscalated = instanceEvents.Any(e =>
                    e.EventType == WorkflowEventType.EscalationApplied
                    && e.PayloadJson is not null
                    && e.PayloadJson.Contains(item.Id.ToString(), StringComparison.OrdinalIgnoreCase));

                if (alreadyEscalated)
                    continue;

                var activity = await activityRepo.GetByIdAsync(item.ActivityInstanceId, cancellationToken);
                if (activity is null)
                    continue;

                var instance = await instanceRepo.GetByIdAsync(item.WorkflowInstanceId, cancellationToken);
                if (instance is null || instance.Status != WorkflowInstanceStatus.Running)
                    continue;

                var activityDef = await db.ActivityDefinitions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        a => a.WorkflowVersionId == instance.PinnedWorkflowVersionId
                             && a.NodeKey == activity.ActivityNodeKey,
                        cancellationToken);

                var policy = await ResolveSlaPolicyAsync(
                    slaRepo, item.OrganizationId, activityDef?.ConfigurationJson, cancellationToken);
                using var configuration = JsonDocument.Parse(activityDef?.ConfigurationJson ?? "{}");
                var escalationKey = configuration.RootElement.TryGetProperty("slaEscalationKey", out var configuredKey)
                    ? configuredKey.GetString() : policy?.EscalationAssignmentKey;
                if (string.IsNullOrWhiteSpace(escalationKey))
                    continue;

                var groupResult = await assignmentResolver.ResolveGroupAsync(
                    item.OrganizationId,
                    instance.WorkflowBindingId,
                    escalationKey,
                    cancellationToken);

                if (groupResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Escalation AssignmentKey {Key} not mapped for work item {WorkItemId}: {Error}",
                        escalationKey, item.Id, groupResult.Error.Message);
                    continue;
                }

                if (groupResult.Value == item.AssignmentGroupId)
                    continue;

                item.Reassign(groupResult.Value, now);
                await workItemRepo.SaveChangesAsync(cancellationToken);

                var candidates = await candidateFactory.CreateCandidatesAsync(
                    item.OrganizationId, item.Id, groupResult.Value, now, cancellationToken);
                await candidateRepo.AddRangeAsync(candidates, cancellationToken);

                await events.AppendAsync(
                    item.OrganizationId,
                    item.WorkflowInstanceId,
                    WorkflowEventType.EscalationApplied,
                    now,
                    activityNodeKey: activity.ActivityNodeKey,
                    payloadJson: JsonSerializer.Serialize(new { workItemId = item.Id, newGroupId = groupResult.Value, policyCode = policy?.PolicyCode }),
                    cancellationToken: cancellationToken);

                await events.AppendAsync(
                    item.OrganizationId,
                    item.WorkflowInstanceId,
                    WorkflowEventType.WorkItemReassigned,
                    now,
                    activityNodeKey: activity.ActivityNodeKey,
                    payloadJson: $"{{\"workItemId\":\"{item.Id}\",\"assignmentGroupId\":\"{groupResult.Value}\"}}",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception escalating work item {WorkItemId}", item.Id);
            }
        }
    }

    private static async Task<Domain.Entities.SlaPolicy?> ResolveSlaPolicyAsync(
        ISlaPolicyRepository slaRepo,
        Guid organizationId,
        string? configurationJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("slaPolicyId", out var idProp)
                && idProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(idProp.GetString(), out var policyId))
            {
                return await slaRepo.GetByIdAsync(policyId, cancellationToken);
            }

            if (root.TryGetProperty("slaPolicyCode", out var codeProp)
                && codeProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(codeProp.GetString()))
            {
                return await slaRepo.GetByCodeAsync(codeProp.GetString()!, organizationId, cancellationToken);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
