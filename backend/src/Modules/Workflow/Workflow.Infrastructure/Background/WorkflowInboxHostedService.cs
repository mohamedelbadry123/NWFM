namespace Workflow.Infrastructure.Background;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Application.Abstractions;
using Workflow.Domain.Repositories;

/// <summary>
/// Crash-recovery processor for leftover Pending inbox trigger messages.
/// Normal path: TriggerService writes inbox and starts synchronously.
/// </summary>
internal sealed class WorkflowInboxHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private const int BatchSize = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowInboxHostedService> _logger;

    public WorkflowInboxHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowInboxHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow inbox hosted service started (interval={Interval}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in workflow inbox hosted service poll cycle");
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

    internal async Task ProcessPendingBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWorkflowIntegrationInboxRepository>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowRuntimeEngine>();
        var instanceRepo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();

        var pending = await inbox.GetPendingAsync(BatchSize, cancellationToken);
        foreach (var message in pending)
        {
            try
            {
                var now = DateTime.UtcNow;

                if (message.BindingId is null)
                {
                    message.MarkDeadLetter(now, "Missing BindingId on inbox message.");
                    await inbox.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var alreadyStarted = await instanceRepo.ExistsByIdempotencyKeyAsync(
                    message.OrganizationId, message.IdempotencyKey, cancellationToken);
                if (alreadyStarted)
                {
                    var existing = await instanceRepo.GetByIdempotencyKeyAsync(
                        message.OrganizationId, message.IdempotencyKey, cancellationToken);
                    if (existing is not null)
                        message.MarkProcessed(existing.Id, now);
                    else
                        message.MarkDeadLetter(now, "Idempotency key exists but instance could not be loaded.");
                    await inbox.SaveChangesAsync(cancellationToken);
                    continue;
                }

                message.MarkProcessing(now);
                await inbox.SaveChangesAsync(cancellationToken);

                var result = await engine.StartAsync(
                    message.OrganizationId,
                    message.BindingId.Value,
                    message.BusinessEntityId,
                    message.IdempotencyKey,
                    DateTime.UtcNow,
                    message.CorrelationId,
                    startedByUserId: null,
                cancellationToken: cancellationToken);

                if (result.IsFailure)
                {
                    message.MarkFailed(result.Error.Message, DateTime.UtcNow);
                    await inbox.SaveChangesAsync(cancellationToken);
                    _logger.LogWarning(
                        "Inbox recovery StartAsync failed for {MessageId}: {Error}",
                        message.MessageId, result.Error.Message);
                    continue;
                }

                message.MarkProcessed(result.Value.Id, DateTime.UtcNow);
                await inbox.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception processing inbox message {MessageId}", message.MessageId);
                try
                {
                    message.MarkFailed(ex.Message, DateTime.UtcNow);
                    await inbox.SaveChangesAsync(cancellationToken);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to mark inbox message {MessageId} as Failed", message.MessageId);
                }
            }
        }
    }
}

