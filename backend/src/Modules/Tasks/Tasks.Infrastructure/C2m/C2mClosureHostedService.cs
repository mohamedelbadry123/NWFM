using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tasks.Application.C2m;
using Tasks.Domain.Constants;
using Tasks.Infrastructure.Persistence;

namespace Tasks.Infrastructure.C2m;

/// <summary>
/// Sends queued C2M closures: an approval that did not wait for C2M
/// (<see cref="C2mOptions.WaitForAcknowledgement"/> off), and retries of one C2M never answered.
/// </summary>
/// <remarks>
/// The queue is the tasks themselves — approved, <c>C2mStatus = PENDING</c>, last tried at least one
/// interval ago — so it survives restarts and needs no broker. Each task is claimed by saving its
/// attempt time before the call; the row version makes a second instance working the same queue lose
/// that race rather than send the closure twice.
/// </remarks>
internal sealed class C2mClosureHostedService(
    IServiceScopeFactory scopes,
    IOptions<C2mOptions> options,
    TimeProvider clock,
    ILogger<C2mClosureHostedService> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.RetryIntervalSeconds, 30));

        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            try
            {
                await SendQueuedAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database may be down or not migrated yet; the next tick tries again.
                logger.LogError(ex, "Sending queued C2M closures failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendQueuedAsync(TimeSpan interval, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = now - interval;

        List<Guid> queued;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            queued = await db.Tasks
                .AsNoTracking()
                .Where(t => t.Status == TaskStatuses.Approved
                    && t.C2mStatus == C2mClosureStatuses.Pending
                    && (t.C2mLastAttemptAt == null || t.C2mLastAttemptAt <= due))
                .OrderBy(t => t.C2mLastAttemptAt)
                .Select(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        foreach (var taskId in queued)
        {
            await SendOneAsync(taskId, due, ct);
        }
    }

    private async Task SendOneAsync(Guid taskId, DateTime due, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var closure = scope.ServiceProvider.GetRequiredService<TaskC2mClosure>();

        var task = await db.Tasks.Include(t => t.Assignments).FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null
            || task.C2mStatus != C2mClosureStatuses.Pending
            || (task.C2mLastAttemptAt is { } last && last > due))
        {
            return;
        }

        try
        {
            task.ClaimC2mAttempt(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another sender claimed it first.
            return;
        }

        var outcome = await closure.SendAsync(
            task,
            task.CompletedDate ?? clock.GetUtcNow().UtcDateTime,
            timeout: null,
            queueOnTransportFailure: true,
            ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Background C2M closure for task {TaskNumber} ended as {Outcome} ({C2mStatus}). {Message}",
            task.TaskNumber,
            outcome.Kind,
            task.C2mStatus,
            outcome.Message);
    }
}
