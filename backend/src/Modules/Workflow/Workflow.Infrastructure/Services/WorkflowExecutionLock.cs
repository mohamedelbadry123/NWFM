namespace Workflow.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;

internal static class WorkflowExecutionLock
{
    internal static bool IsTransient(WorkflowDbContext db, Exception error)
        => error is DbUpdateConcurrencyException or NWFM.Shared.Exceptions.WorkflowQueueCapacityException || error is SqlException { Number: 51000 }
            || new RetryClassification(db).IsRetryable(error)
            || error.InnerException is not null && IsTransient(db, error.InnerException);

    // Reuse EF's SQL Server deadlock/connection/timeout classification, plus our own sp_getapplock error.
    private sealed class RetryClassification(WorkflowDbContext context) : SqlServerRetryingExecutionStrategy(context)
    {
        public bool IsRetryable(Exception error) => ShouldRetryOn(error);
    }

    public static async Task<T> RunAsync<T>(WorkflowDbContext db, string key, Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return await action();
        if (key.StartsWith("instance:", StringComparison.Ordinal) && Guid.TryParse(key[9..], out var instanceId))
            key = "instance:" + await WorkflowTreeGuard.RootAsync(db, instanceId, ct);
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        var resource = "nwfm:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'Workflow is busy. Retry this request.', 1;", ct);
        var result = await action();
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }
}
