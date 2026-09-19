namespace Workflow.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Workflow.Infrastructure.Persistence;

internal static class WorkflowExecutionLock
{
    public static async Task<T> RunAsync<T>(WorkflowDbContext db, string key, Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return await action();
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
