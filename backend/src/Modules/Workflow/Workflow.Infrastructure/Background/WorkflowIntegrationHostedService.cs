namespace Workflow.Infrastructure.Background;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Services;

internal sealed class WorkflowIntegrationHostedService(IServiceScopeFactory scopes, ILogger<WorkflowIntegrationHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(PollAsync(true, stoppingToken), PollAsync(false, stoppingToken));

    internal async Task ProcessDesignerRecoveryAsync(CancellationToken ct)
    {
        using var routingScope = scopes.CreateScope();
        if (await WorkflowTenantAdmission.IsActiveAsync(routingScope.ServiceProvider, ct))
            await routingScope.ServiceProvider.GetRequiredService<WorkflowRuntimeEngine>().ProcessRoutingDeadlinesAsync(ct,
                (tenant, token) => WorkflowTenantAdmission.RequireMessageScopeAsync(routingScope.ServiceProvider, tenant, token));
        using var mappingScope = scopes.CreateScope();
        if (await WorkflowTenantAdmission.IsActiveAsync(mappingScope.ServiceProvider, ct))
            await mappingScope.ServiceProvider.GetRequiredService<WorkflowMappingRuntime>().RecoverAsync(ct,
                (tenant, token) => WorkflowTenantAdmission.RequireMessageScopeAsync(mappingScope.ServiceProvider, tenant, token));
    }

    private async Task PollAsync(bool delivery, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!await WorkflowTenantAdmission.IsActiveAsync(scope.ServiceProvider, stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
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
                        if (!await WorkflowTenantAdmission.IsActiveAsync(jobScope.ServiceProvider, ct)) return;
                        try { await jobScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessJobAsync(id, ct); }
                        catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Integration operation {OperationId} will recover on the next poll.", id); }
                    });
                }
                else
                {
                    using var eventScope = scopes.CreateScope();
                    if (await WorkflowTenantAdmission.IsActiveAsync(eventScope.ServiceProvider, stoppingToken))
                        await eventScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessEventsAsync(stoppingToken);
                    using var childScope = scopes.CreateScope();
                    if (await WorkflowTenantAdmission.IsActiveAsync(childScope.ServiceProvider, stoppingToken))
                        await childScope.ServiceProvider.GetRequiredService<WorkflowIntegrationProcessor>().ProcessChildrenAsync(stoppingToken);
                    using var workspaceScope = scopes.CreateScope();
                    if (await WorkflowTenantAdmission.IsActiveAsync(workspaceScope.ServiceProvider, stoppingToken))
                        await workspaceScope.ServiceProvider.GetRequiredService<WorkflowWorkspaceProcessor>().ProcessAsync(stoppingToken);
                    await ProcessDesignerRecoveryAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Workflow integration processing failed; persisted work remains recoverable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
