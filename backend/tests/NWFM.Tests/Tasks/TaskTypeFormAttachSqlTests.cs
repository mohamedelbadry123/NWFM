namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Application.Constants;
using global::Tasks.Application.TaskTypes.Commands.AttachTaskTypeForm;
using global::Tasks.Application.TaskTypes.Models;
using global::Tasks.Domain.Entities;
using global::Tasks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Tests.Infrastructure;

public sealed class AttachSqlFactAttribute : FactAttribute
{
    public AttachSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NWFM_ATTACH_SQL_TESTS") != "1")
            Skip = "Opt in to owned LocalDB attach concurrency tests with NWFM_ATTACH_SQL_TESTS=1.";
    }
}

public sealed class TaskTypeFormAttachSqlTests
{
    // Pause only the first two saves, after both production handlers have read and changed the type.
    // All SQL, transactions, rowversion checks, failures and retries remain the production EF path.
    private sealed class ConcurrentSaves : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private int _failures;
        public List<(Guid Context, string Version)> Snapshots { get; } = [];
        public int Failures => _failures;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) <= 2)
            {
                var db = eventData.Context!;
                var type = db.ChangeTracker.Entries<TaskType>().Single().Entity;
                lock (Snapshots) Snapshots.Add((db.ContextId.InstanceId, Convert.ToHexString(type.RowVersion)));
                lock (Snapshots) if (Snapshots.Count == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _failures);
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _failures);
            return ValueTask.FromResult(result);
        }

        public void AssertRealRace()
        {
            Snapshots.Select(s => s.Context).Should().OnlyHaveUniqueItems().And.HaveCount(2);
            Snapshots.Select(s => s.Version).Distinct().Should().ContainSingle().Which.Should().NotBeEmpty();
            Failures.Should().BeGreaterThanOrEqualTo(1, "SQL must reject a stale writer before the production handler retries");
        }
    }

    private static TasksDbContext Context(OwnedLocalDb sql, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(sql.ConnectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "TK")).AddInterceptors(interceptors).Options);

    private static AttachTaskTypeFormCommandHandler Handler(TasksDbContext db)
    {
        var forms = new Mock<IFormGateway>();
        forms.Setup(f => f.FindPublishedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PublishedFormInfo(id, "FORM", "Form", "Form", "SURVEY", "PUBLISHED", 1, true));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.UserName).Returns("sql-writer");
        return new(db, forms.Object, user.Object, TimeProvider.System);
    }

    private static async Task<TaskType> Seed(OwnedLocalDb sql, Guid[] forms)
    {
        await using var db = Context(sql);
        await db.Database.MigrateAsync();
        var type = TaskType.Create("SQL", "SQL", "SQL", null, null, forms, forms[1], "seed", TaskTestData.Now);
        db.TaskTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    private static async Task<Result<TaskTypeDto>[]> Race(OwnedLocalDb sql, Guid type, Guid first, Guid second)
    {
        var rendezvous = new ConcurrentSaves();
        await using var writer = Context(sql, rendezvous);
        await using var other = Context(sql, rendezvous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await Task.WhenAll(
            Handler(writer).Handle(new(type, first), timeout.Token),
            Handler(other).Handle(new(type, second), timeout.Token));
        rendezvous.AssertRealRace();
        return results;
    }

    private static async Task<TaskTypeForm[]> Rows(OwnedLocalDb sql, Guid type)
    {
        await using var db = Context(sql);
        return await db.TaskTypeForms.AsNoTracking().Where(f => f.TaskTypeId == type).OrderBy(f => f.SortOrder).ToArrayAsync();
    }

    private static void AssertOrderAndClosing(TaskTypeForm[] rows, Guid closing)
    {
        rows.Select(r => r.SortOrder).Should().Equal(Enumerable.Range(0, rows.Length));
        rows.Select(r => r.FormDefinitionId).Should().OnlyHaveUniqueItems();
        rows.Where(r => r.IsC2mClosingForm).Should().ContainSingle().Which.FormDefinitionId.Should().Be(closing);
    }

    [AttachSqlFact]
    public async Task ConcurrentDistinctAndDuplicateAttachesPersistWithoutLosingMembershipOrReordering()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        var original = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var type = await Seed(sql, original);
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var duplicate = Guid.NewGuid();

        var distinctResults = await Race(sql, type.Id, first, second);

        distinctResults.Should().OnlyContain(r => r.IsSuccess);
        var distinctRows = await Rows(sql, type.Id);
        distinctRows.Select(r => r.FormDefinitionId).Should().BeEquivalentTo(original.Concat([first, second]));
        distinctRows.Take(2).Select(r => r.Id).Should().Equal(type.Forms.OrderBy(r => r.SortOrder).Select(r => r.Id));
        distinctRows.Take(2).Select(r => r.FormDefinitionId).Should().Equal(original);
        AssertOrderAndClosing(distinctRows, original[1]);

        var duplicateResults = await Race(sql, type.Id, duplicate, duplicate);

        duplicateResults.Should().OnlyContain(r => r.IsSuccess);
        var finalRows = await Rows(sql, type.Id);
        finalRows.Should().HaveCount(5);
        finalRows.Count(r => r.FormDefinitionId == duplicate).Should().Be(1);
        finalRows.Take(4).Select(r => (r.Id, r.SortOrder, r.UpdatedAt)).Should()
            .Equal(distinctRows.Select(r => (r.Id, r.SortOrder, r.UpdatedAt)));
        finalRows.Last().FormDefinitionId.Should().Be(duplicate);
        AssertOrderAndClosing(finalRows, original[1]);
        foreach (var result in duplicateResults)
            result.Value.Forms.Select(f => f.FormDefinitionId).Should().Equal(finalRows.Select(r => r.FormDefinitionId));
    }

    [AttachSqlFact]
    public async Task ConcurrentLastSlotAcceptsOnlyOneFormAndDuplicateAtCapDoesNotWrite()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        var original = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        var type = await Seed(sql, original);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();

        var results = await Race(sql, type.Id, first, second);

        results.Should().ContainSingle(r => r.IsSuccess);
        results.Should().ContainSingle(r => r.IsFailure).Which.Error.Code.Should().Be(TaskErrors.Codes.TypeInvalid);
        var rows = await Rows(sql, type.Id);
        rows.Should().HaveCount(10);
        rows.Take(9).Select(r => r.FormDefinitionId).Should().Equal(original);
        rows.Take(9).Select(r => r.Id).Should().Equal(type.Forms.OrderBy(r => r.SortOrder).Select(r => r.Id));
        new[] { first, second }.Should().Contain(rows.Last().FormDefinitionId);
        results.Single(r => r.IsSuccess).Value.Forms.Select(f => f.FormDefinitionId).Should().Equal(rows.Select(r => r.FormDefinitionId));
        AssertOrderAndClosing(rows, original[1]);
        await using var db = Context(sql);
        var version = (await db.TaskTypes.AsNoTracking().SingleAsync()).RowVersion;

        var again = await Handler(db).Handle(new(type.Id, rows.Last().FormDefinitionId), default);

        again.IsSuccess.Should().BeTrue();
        (await Rows(sql, type.Id)).Select(r => (r.Id, r.SortOrder, r.UpdatedAt, r.IsC2mClosingForm)).Should()
            .Equal(rows.Select(r => (r.Id, r.SortOrder, r.UpdatedAt, r.IsC2mClosingForm)));
        (await db.TaskTypes.AsNoTracking().SingleAsync()).RowVersion.Should().Equal(version);
    }
}
