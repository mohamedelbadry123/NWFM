namespace Workflow.Infrastructure.Background;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Services;
using Workflow.Domain.Enums;

internal sealed class WorkflowIntegrationHostedService(IServiceScopeFactory scopes, ILogger<WorkflowIntegrationHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(PollAsync(true, stoppingToken), PollAsync(false, stoppingToken));

    private async Task PollAsync(bool delivery, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
                if (delivery)
                {
                var now = DateTime.UtcNow;
                var jobs = await db.IntegrationJobs.AsNoTracking().Where(j => (j.Status == "Delivered"
                    || j.Status == "Pending" && j.NextAttemptAt <= now || j.Status == "Running" && j.LeaseUntil <= now)
                    && db.WorkflowInstances.Any(i => i.Id == j.WorkflowInstanceId && i.Status != WorkflowInstanceStatus.Suspended
                        && (i.Status != WorkflowInstanceStatus.Failed || j.IsActivityEvent && !j.Required)))
                    .OrderBy(j => j.NextAttemptAt).Select(j => j.Id).Take(25).ToListAsync(stoppingToken);
                await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken }, async (id, ct) =>
                {
                    using var jobScope = scopes.CreateScope();
                    try { await jobScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessJobAsync(id, ct); }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Integration operation {OperationId} will recover on the next poll.", id); }
                });
                }
                else
                {
                using var eventScope = scopes.CreateScope();
                await eventScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessEventsAsync(stoppingToken);
                using var childScope = scopes.CreateScope();
                await childScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessChildrenAsync(stoppingToken);
                using var workspaceScope = scopes.CreateScope();
                await workspaceScope.ServiceProvider.GetRequiredService<WorkflowWorkspaceProcessor>().ProcessAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Workflow integration processing failed; persisted work remains recoverable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
