namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Application.C2m;
using global::Tasks.Application.Common;
using global::Tasks.Application.Constants;
using global::Tasks.Application.Tasks.Commands.CompleteTask;
using global::Tasks.Domain.Constants;
using global::Tasks.Domain.Entities;
using global::Tasks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Caching;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Options;
using NWFM.Shared.Organization;
using NWFM.Tests.Modules.FormEngine;

/// <summary>
/// Closing a task's C2M field activity: what the Action Taken answer resolves to, what is sent, and
/// what approving the task does when C2M accepts, refuses, cannot be reached, or is switched off.
/// </summary>
public sealed class C2mClosureTests : IDisposable
{
    private static readonly DateTime Now = TaskTestData.Now;

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IOrgScopeProvider> _scopes = new();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<IC2mClient> _client = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private C2mOptions _options = new() { Enabled = true, WaitForAcknowledgement = true };
    private C2mClosureRequest? _sent;

    /// <summary>The closing form: an Action Taken field, two parameters, and a field C2M never hears about.</summary>
    private static readonly IReadOnlyList<FormFieldInfo> Fields =
    [
        new(C2mFieldNames.ActionTaken, "single_choice", "Action taken", "الإجراء", null,
        [
            new("DONE", "Done", "تم", C2mOperationStatuses.Completed, "CLOSED-OK"),
            new("MMFCNR2", "Obstacles", "عوائق", null, null),
        ]),
        new("wfm_meter_reading", "numeric", "Reading", "القراءة", "CM-MTRRD", []),
        new("wfm_building_units", "numeric", "Units", "الوحدات", "CM_BUNIT", []),
        new("inspector_note", "text", "Note", "ملاحظة", null, []),
    ];

    public C2mClosureTests()
    {
        _user.SetupGet(u => u.UserName).Returns("reviewer");
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.Unrestricted());
        _forms.Setup(f => f.GetFieldsAsync(TaskTestData.FormId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Fields);
        Answers(new Dictionary<string, object?>
        {
            [C2mFieldNames.ActionTaken] = "MMFCNR2",
            ["wfm_meter_reading"] = 1250.5000m,
            ["wfm_building_units"] = null,
            ["inspector_note"] = "not sent",
            [C2mFieldNames.Remarks] = "Gate locked",
        });
        C2mAnswers(status: "OK", code: "0");
    }

    public void Dispose() => _db.Dispose();

    private void Answers(IReadOnlyDictionary<string, object?> answers) =>
        _forms.Setup(f => f.GetLatestByContextAsync(TaskTestData.FormId, TasksSchema.FormContextType, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), TaskTestData.FormId, 1, "crew", "Crew A", Now, answers));

    private void C2mAnswers(string status, string code, string? error = null) =>
        _client.Setup(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()))
            .Callback<C2mClosureRequest, CancellationToken>((request, _) => _sent = request)
            .ReturnsAsync(new C2mClosureResponse { Status = status, ResponseCode = code, ErrorDescription = error });

    private C2mActionMappingResolver Resolver() =>
        new(_db, new PassThroughCache(), Options.Create(new CacheSettings()));

    private TaskC2mClosure Closure()
    {
        var options = Options.Create(_options);
        var dispatcher = new C2mDispatcher(_db, _forms.Object, _client.Object, Resolver(), options, _clock, NullLogger<C2mDispatcher>.Instance);
        return new TaskC2mClosure(_db, dispatcher, options, _clock);
    }

    private CompleteTaskCommandHandler Handler() =>
        new(_db, new TaskAccess(_db, _scopes.Object, _user.Object), Closure(), _user.Object, _clock);

    /// <summary>A filled task of a closing type, carrying FA-1001 and WFM ticket 77.</summary>
    private async Task<Guid> SeedFilledTaskAsync(bool typeCloses = true, string? faId = "FA-1001")
    {
        var type = TaskTestData.Type();
        type.SetClosesC2mActivity(typeCloses, "admin", Now);

        var task = TaskTestData.Task(type);
        task.SetFieldActivity(faId, 77, "admin", Now);
        task.Assign(Guid.NewGuid(), "supervisor", null, null, null, Now);
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        _db.TaskTypes.Add(type);
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return task.Id;
    }

    private async Task<FieldTask> ReloadAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        return await _db.Tasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    [Fact]
    public async Task Resolver_AnOptionThatNamesAStatusWins()
    {
        var outcome = await Resolver().ResolveAsync("done", Fields, CancellationToken.None);

        outcome.Should().Be(new C2mActionOutcome(C2mOperationStatuses.Completed, null, "CLOSED-OK"));
    }

    [Fact]
    public async Task Resolver_FallsBackToTheMappingTable()
    {
        _db.C2mActionMappings.Add(C2mActionMapping.Create("MMFCNR2", "X", "OBSTACLE", null, "Obstacles", "عوائق", true, "admin", Now));
        await _db.SaveChangesAsync();

        var outcome = await Resolver().ResolveAsync("MMFCNR2", Fields, CancellationToken.None);

        outcome.Should().Be(new C2mActionOutcome(C2mOperationStatuses.Cancelled, "OBSTACLE", null));
    }

    [Fact]
    public async Task Resolver_AnUnknownCodeCancelsCarryingItselfAsTheReason()
    {
        var outcome = await Resolver().ResolveAsync("ZZ9", Fields, CancellationToken.None);

        outcome.Should().Be(new C2mActionOutcome(C2mOperationStatuses.Cancelled, "ZZ9", null));
    }

    [Fact]
    public async Task Resolver_TheBuiltInCompletedCodeCompletesWithNoMapping()
    {
        var outcome = await Resolver().ResolveAsync(C2mOperationStatuses.CompletedActionCode, Fields, CancellationToken.None);

        outcome.FaStatus.Should().Be(C2mOperationStatuses.Completed);
        outcome.CancelReason.Should().BeNull();
    }

    [Fact]
    public async Task Complete_WaitsForC2mAndApprovesWhenItAccepts()
    {
        var id = await SeedFilledTaskAsync();

        var result = await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = await ReloadAsync(id);
        task.Status.Should().Be(TaskStatuses.Approved);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Closed);
        (await _db.C2mDispatchLogs.SingleAsync()).Status.Should().Be(C2mDispatchStatuses.Succeeded);

        _sent!.FaDetails.FaId.Should().Be("FA-1001");
        _sent.FaDetails.FaStatus.Should().Be(C2mOperationStatuses.Cancelled);
        _sent.FaDetails.CancelReason.Should().Be("MMFCNR2");
        _sent.FaDetails.MobId.Should().Be("77");
        _sent.FaDetails.Comment.Should().Be("Gate locked");
        _sent.FaDetails.CompletionDttm.Should().Be("2026-09-21-08.00.00");
        // Only named fields that were answered travel; the decimal loses SQL's trailing zeros.
        _sent.FaDetails.ParametersList.Parameters.Should().ContainSingle()
            .Which.Should().Be(new C2mParameter { ParameterName = "CM-MTRRD", ParameterValue = "1250.5" });
    }

    [Fact]
    public async Task Complete_RefusedByC2m_LeavesTheTaskFilledAndRecordsTheAttempt()
    {
        var id = await SeedFilledTaskAsync();
        C2mAnswers(status: "ERROR", code: "12", error: "FA already closed");

        var result = await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(TaskErrors.Codes.C2mRejected);
        var task = await ReloadAsync(id);
        task.Status.Should().Be(TaskStatuses.Submitted);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Rejected);
        var log = await _db.C2mDispatchLogs.SingleAsync();
        log.Status.Should().Be(C2mDispatchStatuses.Failed);
        log.ErrorMessage.Should().Be("FA already closed");
    }

    [Fact]
    public async Task Complete_C2mUnreachable_IsReportedAsUnavailable()
    {
        var id = await SeedFilledTaskAsync();
        _client.Setup(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        result.Error.Code.Should().Be(TaskErrors.Codes.C2mUnavailable);
        (await ReloadAsync(id)).Status.Should().Be(TaskStatuses.Submitted);
    }

    [Fact]
    public async Task Complete_IntegrationOff_ApprovesAndRecordsTheSkip()
    {
        _options = new C2mOptions { Enabled = false };
        var id = await SeedFilledTaskAsync();

        var result = await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = await ReloadAsync(id);
        task.Status.Should().Be(TaskStatuses.Approved);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Skipped);
        (await _db.C2mDispatchLogs.SingleAsync()).Status.Should().Be(C2mDispatchStatuses.Skipped);
        _client.Verify(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_NotWaiting_ApprovesAndQueuesTheClosure()
    {
        _options = new C2mOptions { Enabled = true, WaitForAcknowledgement = false };
        var id = await SeedFilledTaskAsync();

        await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        var task = await ReloadAsync(id);
        task.Status.Should().Be(TaskStatuses.Approved);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Pending);
        _client.Verify(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, "FA-1001")]
    [InlineData(true, null)]
    public async Task Complete_OnlyAClosingTypeWithAnFaIdClosesAnything(bool typeCloses, string? faId)
    {
        var id = await SeedFilledTaskAsync(typeCloses, faId);

        await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        var task = await ReloadAsync(id);
        task.Status.Should().Be(TaskStatuses.Approved);
        task.C2mStatus.Should().BeNull();
        (await _db.C2mDispatchLogs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Background_AnUnansweredClosureStaysQueuedUntilTheAttemptsRunOut()
    {
        _options = new C2mOptions { Enabled = true, MaxAttempts = 2 };
        var id = await SeedFilledTaskAsync();
        _client.Setup(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        var task = await _db.Tasks.SingleAsync(t => t.Id == id);
        await Closure().SendAsync(task, Now, null, queueOnTransportFailure: true, CancellationToken.None);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Pending);

        await _db.SaveChangesAsync();
        await Closure().SendAsync(task, Now, null, queueOnTransportFailure: true, CancellationToken.None);
        task.C2mStatus.Should().Be(C2mClosureStatuses.Failed);
        task.C2mAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Dispatch_AnAcceptedClosureIsNeverSentTwice()
    {
        var id = await SeedFilledTaskAsync();
        await Handler().Handle(new CompleteTaskCommand { TaskId = id }, CancellationToken.None);

        var task = await _db.Tasks.SingleAsync(t => t.Id == id);
        var outcome = await Closure().SendAsync(task, Now, null, queueOnTransportFailure: true, CancellationToken.None);

        outcome.Kind.Should().Be(C2mDispatchOutcomeKind.AlreadyAcknowledged);
        _client.Verify(c => c.CloseFieldActivityAsync(It.IsAny<C2mClosureRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>No caching — every read goes to the factory, so a test sees what it just wrote.</summary>
    private sealed class PassThroughCache : ICacheService
    {
        public ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CacheEntryOptions? options = null, CancellationToken cancellationToken = default) =>
            factory(cancellationToken);

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
