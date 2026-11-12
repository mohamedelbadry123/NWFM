namespace Workflow.Infrastructure.Persistence;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed partial class WorkflowDbContext
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveWithQueueCapacityAsync(acceptAllChangesOnSuccess, asynchronous: false, default).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        SaveWithQueueCapacityAsync(acceptAllChangesOnSuccess, asynchronous: true, cancellationToken);

    private async Task<int> SaveWithQueueCapacityAsync(bool acceptAll, bool asynchronous, CancellationToken ct)
    {
        // Only transitions into an active queue consume capacity. Existing active rows
        // that change lease/retry metadata must not be counted twice.
        var admissions = new[]
        {
            Admission<WorkflowIntegrationInbox>(x => x.Status == WorkflowInboxStatus.Pending || x.Status == WorkflowInboxStatus.Processing),
            Admission<WorkflowIntegrationOutbox>(x => x.Status == WorkflowOutboxStatus.Pending),
            Admission<WorkflowIntegrationJob>(x => x.Status == "Pending" || x.Status == "Running" || x.Status == "Delivered"),
            Admission<WorkflowMappingInvocation>(x => x.Status == "Preparing" || x.Status == "Waiting" || x.Status == "Prepared"
                || x.Status == "Queued" || x.Status == "Delivered"
                || x.Status == "Attention" && !(x.ExecutionKind == "Graph" && x.GraphAdvanced)),
            // Join Attention is terminal: ResolveRoutingGateAsync refuses subsequent arrivals,
            // and no routing retry endpoint resets it. Callable/OptionalGraph Attention remains replayable.
            Admission<WorkflowRoutingActivation>(x => x.Status == "Waiting"),
            Admission<WorkflowEventReceipt>(x => x.Status == "Received"),
            Admission<WorkflowEventSubscription>(x => x.Status == "Waiting"),
            Admission<WorkflowTimer>(x => x.Status == WorkflowTimerStatus.Pending || x.Status == WorkflowTimerStatus.Fired),
        }.Where(x => x.Additions > 0).ToArray();

        var needsTransaction = admissions.Length > 0 && Database.IsSqlServer();
        await using var transaction = needsTransaction && Database.CurrentTransaction is null
            ? asynchronous ? await Database.BeginTransactionAsync(ct).ConfigureAwait(false) : Database.BeginTransaction()
            : null;
        if (needsTransaction)
        {
            // One admission lock per dedicated tenant database; retained only to the
            // enclosing transaction's commit/rollback, including nested workflow writes.
            var resource = "nwfm:queue-capacity:" + CurrentTenant.OrganizationId.ToString("N");
            FormattableString command = $"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @r < 0 THROW 51000, 'Workflow queue is busy. Retry this request.', 1;";
            if (asynchronous) await Database.ExecuteSqlInterpolatedAsync(command, ct).ConfigureAwait(false);
            else Database.ExecuteSqlInterpolated(command);
        }

        foreach (var admission in admissions)
        {
            var persisted = await admission.Count(asynchronous, ct).ConfigureAwait(false);
            if ((long)persisted + admission.Additions > _maximumPendingPerQueue)
                throw new WorkflowQueueCapacityException(admission.Name);
        }

        // Reservations and queue writes share this transaction and the base tenant write guard.
        if (asynchronous) await PrepareReceiptReservationsAsync(ct).ConfigureAwait(false);
        else PrepareReceiptReservations();

        var result = asynchronous
            ? await base.SaveChangesAsync(acceptAll, ct).ConfigureAwait(false)
            : base.SaveChanges(acceptAll);
        if (transaction is not null)
        {
            if (asynchronous) await transaction.CommitAsync(ct).ConfigureAwait(false);
            else transaction.Commit();
        }
        return result;
    }

    private QueueAdmission Admission<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        // Use the complete predicate for both snapshots. Status alone misses eligibility changes
        // such as GraphAdvanced becoming true while Status remains Attention.
        var active = predicate.Compile(preferInterpretation: true);
        var additions = ChangeTracker.Entries<TEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Sum(entry =>
                (entry.State != EntityState.Deleted && active((TEntity)entry.CurrentValues.ToObject()) ? 1 : 0)
                - (entry.State != EntityState.Added && active((TEntity)entry.OriginalValues.ToObject()) ? 1 : 0));
        var query = Set<TEntity>().AsNoTracking().Where(predicate);
        return new QueueAdmission(typeof(TEntity).Name, additions,
            (asynchronous, ct) => asynchronous ? query.CountAsync(ct) : Task.FromResult(query.Count()));
    }

    private sealed record QueueAdmission(string Name, int Additions, Func<bool, CancellationToken, Task<int>> Count);
}
