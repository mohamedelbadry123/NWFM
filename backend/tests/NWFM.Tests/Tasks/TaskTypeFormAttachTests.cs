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
using NWFM.Shared.Behaviors;
using NWFM.Shared.Constants;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Tests.Modules.FormEngine;

/// <summary>Filing one published form under a task type: additive, idempotent, and guarded by both permissions.</summary>
public sealed class TaskTypeFormAttachTests : IDisposable
{
    private static readonly Guid FormA = TaskTestData.FormId;
    private static readonly Guid FormB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FormC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly AttachTaskTypeFormCommandHandler _handler;

    public TaskTypeFormAttachTests()
    {
        _user.SetupGet(u => u.UserName).Returns("tester");
        foreach (var id in new[] { FormA, FormB, FormC }) Published(id);
        _handler = new(_db, _forms.Object, _user.Object, new FakeTimeProvider(TaskTestData.Now));
    }

    public void Dispose() => _db.Dispose();

    private void Published(Guid id, bool accepts = true) =>
        _forms.Setup(f => f.FindPublishedAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(id, id.ToString("N")[..4], "Form", "نموذج", "SURVEY", "PUBLISHED", 2, accepts));

    private async Task<TaskType> StoredType(params Guid[] forms)
    {
        var type = TaskTestData.Type(forms);
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return type;
    }

    private async Task<List<TaskTypeForm>> Stored(Guid typeId) =>
        await _db.TaskTypeForms.AsNoTracking().Where(f => f.TaskTypeId == typeId).OrderBy(f => f.SortOrder).ToListAsync();

    [Fact]
    public async Task Attach_AppendsAfterTheExistingFormsAndKeepsTheirOrderAndTheClosingForm()
    {
        var type = await StoredType(FormA, FormB);

        var result = await _handler.Handle(new(type.Id, FormC), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Forms.Select(f => f.FormDefinitionId).Should().Equal(FormA, FormB, FormC);
        var rows = await Stored(type.Id);
        rows.Select(r => r.SortOrder).Should().Equal(0, 1, 2);
        rows.Where(r => r.IsC2mClosingForm).Select(r => r.FormDefinitionId).Should().Equal(FormA);
    }

    [Fact]
    public async Task Attach_IsIdempotentAndWritesNothingTheSecondTime()
    {
        var type = await StoredType(FormA);
        await _handler.Handle(new(type.Id, FormB), default);
        var before = await Stored(type.Id);

        var again = await _handler.Handle(new(type.Id, FormB), default);

        again.IsSuccess.Should().BeTrue();
        again.Value.Forms.Should().HaveCount(2);
        var after = await Stored(type.Id);
        after.Select(r => (r.Id, r.SortOrder, r.UpdatedAt)).Should().Equal(before.Select(r => (r.Id, r.SortOrder, r.UpdatedAt)));
    }

    [Fact]
    public async Task Attach_KeepsMembershipsFiledByEarlierWritersInsteadOfRewritingTheList()
    {
        var type = await StoredType(FormA);
        // The in-memory store cannot interleave two saves, so this checks the property the retry relies on:
        // each attach starts from what is stored and adds one row, never a replacement list.
        await _handler.Handle(new(type.Id, FormB), default);
        var second = await _handler.Handle(new(type.Id, FormC), default);

        second.Value.Forms.Select(f => f.FormDefinitionId).Should().Equal(FormA, FormB, FormC);
        (await Stored(type.Id)).Select(r => r.FormDefinitionId).Distinct().Should().HaveCount(3);
    }

    /// <summary>Another writer commits (<paramref name="beforeFailure"/>), then this save loses the race, the first <paramref name="failures"/> times.</summary>
    private sealed class LoseTheRace(Action beforeFailure, int failures) : SaveChangesInterceptor
    {
        public int Saves { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Saves++ < failures)
            {
                beforeFailure();
                throw new DbUpdateConcurrencyException("The type was changed by another writer.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private (TasksDbContext Writer, TasksDbContext Other, LoseTheRace Race) RacingContexts(Action<TasksDbContext> otherWriter, int failures)
    {
        var store = Guid.NewGuid().ToString();
        TasksDbContext Open(params IInterceptor[] interceptors) => new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(store).AddInterceptors(interceptors).Options);
        var other = Open();
        var race = new LoseTheRace(() => otherWriter(other), failures);
        return (Open(race), other, race);
    }

    [Fact]
    public async Task Attach_RereadsTheStoredFormsAfterLosingARace_SoAConcurrentMembershipIsKept()
    {
        var (writer, other, race) = RacingContexts(
            o =>
            {
                var type = o.TaskTypes.Include(t => t.Forms).Single();
                type.AttachForm(FormB, "other", TaskTestData.Now);
                o.SaveChanges();
            },
            failures: 1);
        var seeded = TaskTestData.Type(FormA);
        other.TaskTypes.Add(seeded);
        await other.SaveChangesAsync();
        other.ChangeTracker.Clear();
        var handler = new AttachTaskTypeFormCommandHandler(writer, _forms.Object, _user.Object, new FakeTimeProvider(TaskTestData.Now));

        var result = await handler.Handle(new(seeded.Id, FormC), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        race.Saves.Should().Be(2);
        var rows = await other.TaskTypeForms.AsNoTracking().OrderBy(f => f.SortOrder).ToListAsync();
        rows.Select(r => r.FormDefinitionId).Should().Equal(FormA, FormB, FormC);
        rows.Select(r => r.SortOrder).Should().Equal(0, 1, 2);
        rows.Where(r => r.IsC2mClosingForm).Select(r => r.FormDefinitionId).Should().Equal(FormA);
        result.Value.Forms.Select(f => f.FormDefinitionId).Should().Equal(FormA, FormB, FormC);
    }

    [Fact]
    public async Task Attach_ReportsAConflictInsteadOfOverwritingWhenTheRaceIsLostEveryTime()
    {
        var (writer, other, _) = RacingContexts(_ => { }, failures: int.MaxValue);
        var seeded = TaskTestData.Type(FormA);
        other.TaskTypes.Add(seeded);
        await other.SaveChangesAsync();
        var handler = new AttachTaskTypeFormCommandHandler(writer, _forms.Object, _user.Object, new FakeTimeProvider(TaskTestData.Now));

        var result = await handler.Handle(new(seeded.Id, FormC), default);

        result.Error.Should().Be(TaskErrors.Task.ConcurrencyConflict);
        (await other.TaskTypeForms.AsNoTracking().ToListAsync()).Should().ContainSingle().Which.FormDefinitionId.Should().Be(FormA);
    }

    [Fact]
    public async Task Attach_EnforcesTheTenFormLimit()
    {
        var existing = Enumerable.Range(0, TaskType.MaxForms).Select(_ => Guid.NewGuid()).ToArray();
        var type = await StoredType(existing);

        var result = await _handler.Handle(new(type.Id, FormB), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TaskErrors.Codes.TypeInvalid);
        (await Stored(type.Id)).Should().HaveCount(TaskType.MaxForms);
        // Already filed is still fine at the limit.
        _forms.Setup(f => f.FindPublishedAsync(existing[0], It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(existing[0], "X", "X", "X", "SURVEY", "PUBLISHED", 1, true));
        (await _handler.Handle(new(type.Id, existing[0]), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Attach_RefusesAFormThatCannotTakeFills_AndAnInactiveOrMissingType()
    {
        var type = await StoredType(FormA);
        var draft = Guid.NewGuid();
        Published(draft, accepts: false);
        var unknown = Guid.NewGuid();
        _forms.Setup(f => f.FindPublishedAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync((PublishedFormInfo?)null);

        (await _handler.Handle(new(type.Id, draft), default)).Error.Should().Be(TaskErrors.Form.NotPublished);
        (await _handler.Handle(new(type.Id, unknown), default)).Error.Should().Be(TaskErrors.Form.NotFound);
        (await _handler.Handle(new(Guid.NewGuid(), FormB), default)).Error.Should().Be(TaskErrors.Type.NotFound);

        var stored = await _db.TaskTypes.Include(t => t.Forms).SingleAsync();
        stored.SetActive(false, "tester", TaskTestData.Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        (await _handler.Handle(new(type.Id, FormB), default)).Error.Should().Be(TaskErrors.Type.Inactive);
        (await Stored(type.Id)).Should().ContainSingle();
    }

    [Fact]
    public void AttachForm_OnTheEntityNeverReplacesTheCollection()
    {
        var type = TaskTestData.Type(FormA, FormB);
        var rows = type.Forms.ToArray();

        type.AttachForm(FormC, "tester", TaskTestData.Now).Should().BeTrue();
        type.AttachForm(FormC, "tester", TaskTestData.Now).Should().BeFalse();

        type.Forms.Should().Contain(rows);
        type.FormIds.Should().Equal(FormA, FormB, FormC);
        FluentActions.Invoking(() => type.AttachForm(Guid.Empty, null, TaskTestData.Now)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Command_RequiresBothManageFormsAndManageTaskTypes(bool manageForms, bool manageTaskTypes, bool allowed)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns("u1");
        user.Setup(u => u.IsInRole(It.IsAny<string>())).Returns(false);
        user.Setup(u => u.HasPermission(NwfmPolicies.ManageForms)).Returns(manageForms);
        user.Setup(u => u.HasPermission(NwfmPolicies.ManageTaskTypes)).Returns(manageTaskTypes);
        var behavior = new AuthorizationBehavior<AttachTaskTypeFormCommand, Result<TaskTypeDto>>(user.Object);
        var reached = false;

        var call = () => behavior.Handle(new(Guid.NewGuid(), FormB), () => { reached = true; return Task.FromResult(Result.Success(new TaskTypeDto())); }, default);

        if (allowed) await call();
        else await call.Should().ThrowAsync<UnauthorizedAccessException>();
        reached.Should().Be(allowed);
    }
}
