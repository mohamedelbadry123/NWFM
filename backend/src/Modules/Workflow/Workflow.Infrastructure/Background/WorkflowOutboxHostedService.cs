namespace Workflow.Infrastructure.Background;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Integration.Workflow;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

/// <summary>
/// Polls the workflow integration outbox and delivers pending outcome messages
/// to module <see cref="IWorkflowOutcomeHandler"/> implementations.
/// </summary>
internal sealed class WorkflowOutboxHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowOutboxHostedService> _logger;

    public WorkflowOutboxHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowOutboxHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow outbox hosted service started (interval={Interval}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in workflow outbox hosted service poll cycle");
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

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IWorkflowIntegrationOutboxRepository>();
        var handlers = scope.ServiceProvider.GetServices<IWorkflowOutcomeHandler>().ToList();

        var pending = await outbox.GetPendingAsync(BatchSize, cancellationToken);
        foreach (var message in pending)
        {
            try
            {
                var now = DateTime.UtcNow;
                var outcome = ReconstructMessage(message);
                var isShadow = IsShadow(outcome);

                if (!isShadow)
                {
                    var matchingHandlers = handlers.Where(h =>
                            string.Equals(h.ModuleKey, message.ModuleKey, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (matchingHandlers.Length == 0)
                        throw new InvalidOperationException($"No workflow outcome handler is registered for module '{message.ModuleKey}'.");
                    foreach (var handler in matchingHandlers)
                    {
                        await handler.HandleAsync(outcome, cancellationToken);
                    }
                }

                message.MarkPublished(now);
                await outbox.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId}", message.MessageId);
                try
                {
                    message.MarkFailed(DateTime.UtcNow);
                    await outbox.SaveChangesAsync(cancellationToken);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to mark outbox message {MessageId} as Failed", message.MessageId);
                }
            }
        }
    }

    private static WorkflowOutcomeMessage ReconstructMessage(Domain.Entities.WorkflowIntegrationOutbox row)
    {
        Dictionary<string, object?> outputs = new(StringComparer.OrdinalIgnoreCase);
        string? correlationId = row.CorrelationId;
        var occurredAt = row.CreatedAt;

        try
        {
            using var doc = JsonDocument.Parse(row.PayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("ApprovedOutputs", out var ao) && ao.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ao.EnumerateObject())
                    outputs[prop.Name] = prop.Value.Clone();
            }

            if (root.TryGetProperty("CorrelationId", out var cid) && cid.ValueKind == JsonValueKind.String)
                correlationId = cid.GetString() ?? correlationId;

            if (root.TryGetProperty("OccurredAt", out var oa)
                && oa.ValueKind == JsonValueKind.String
                && DateTime.TryParse(oa.GetString(), out var parsed))
            {
                occurredAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }
        catch (JsonException)
        {
            // keep empty outputs
        }

        return new WorkflowOutcomeMessage(
            row.OrganizationId,
            row.BindingId,
            row.WorkflowInstanceId,
            row.ModuleKey,
            row.BusinessEntityType,
            row.BusinessEntityId,
            row.OutcomeKey,
            correlationId ?? row.WorkflowInstanceId.ToString("N"),
            outputs,
            occurredAt,
            row.MessageId);
    }

    private static bool IsShadow(WorkflowOutcomeMessage message)
    {
        if (message.ApprovedOutputs is null)
            return false;

        if (!message.ApprovedOutputs.TryGetValue("IsShadow", out var raw) || raw is null)
            return false;

        return raw switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.String
                => bool.TryParse(je.GetString(), out var parsed) && parsed,
            _ => string.Equals(raw.ToString(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }
}
