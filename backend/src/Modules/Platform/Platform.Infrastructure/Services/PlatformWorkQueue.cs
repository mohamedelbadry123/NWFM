using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Infrastructure.Security;

namespace Platform.Infrastructure.Services;

/// <summary>A bounded in-process queue drained by <see cref="PlatformWorkWorker"/>.</summary>
internal sealed class PlatformWorkQueue : IPlatformWorkQueue
{
    private readonly Channel<(string Name, Func<IServiceProvider, CancellationToken, Task> Work)> _channel =
        Channel.CreateBounded<(string, Func<IServiceProvider, CancellationToken, Task>)>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<(string Name, Func<IServiceProvider, CancellationToken, Task> Work)> Reader => _channel.Reader;

    public bool Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> work) =>
        _channel.Writer.TryWrite((name, work));
}

/// <summary>
/// Drains the platform work queue, and every few minutes keeps the shared key ring current and
/// re-queues signups that a stopped node left queued or mid-provisioning (the provisioner resumes
/// from the last completed step).
/// </summary>
internal sealed class PlatformWorkWorker(
    PlatformWorkQueue queue,
    IServiceScopeFactory scopes,
    ILogger<PlatformWorkWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(DrainAsync(stoppingToken), SweepAsync(stoppingToken));

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        await foreach (var (name, work) in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var platform = scope.ServiceProvider.GetRequiredService<NWFM.Shared.MultiTenancy.ITenantContext>().BeginPlatformScope();
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Platform work item {Name} failed.", name);
            }
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var platform = scope.ServiceProvider.GetRequiredService<NWFM.Shared.MultiTenancy.ITenantContext>().BeginPlatformScope();
                // Rolls the shared key ring over ahead of expiry; the tenant API only reads it.
                KeyRingRotation.EnsureCurrentKey(scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
                var catalog = scope.ServiceProvider.GetRequiredService<ICatalogDbContext>();
                var stuckBefore = DateTime.UtcNow - StuckAfter;
                var pending = await catalog.SignupRequests.AsNoTracking()
                    .Where(s => s.Status == SignupStatus.Queued
                        || s.Status == SignupStatus.Provisioning && s.UpdatedAtUtc < stuckBefore)
                    .Select(s => s.Id)
                    .Take(50)
                    .ToListAsync(stoppingToken);
                var provisioning = scope.ServiceProvider.GetRequiredService<ISignupProvisioningQueue>();
                foreach (var id in pending)
                    provisioning.Enqueue(id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // The catalog may not be migrated yet on the very first start; the next tick retries.
                logger.LogWarning(error, "Re-queuing pending signups failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
