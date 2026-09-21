namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Application.Common;
using global::Tasks.Application.Constants;
using global::Tasks.Application.Tasks.Commands.AssignTask;
using global::Tasks.Application.Tasks.Commands.CreateTask;
using global::Tasks.Application.Tasks.Commands.SubmitTaskFill;
using global::Tasks.Application.Tasks.Queries.GetTaskById;
using global::Tasks.Application.Tasks.Queries.GetTasks;
using global::Tasks.Domain.Constants;
using global::Tasks.Domain.Entities;
using global::Tasks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;
using NWFM.Tests.Modules.FormEngine;

/// <summary>
/// The task handlers against an in-memory context, with the form engine and Auth replaced by their
/// integration contracts — which is all Tasks ever sees of them.
/// </summary>
public sealed class TaskHandlerTests : IDisposable
{
    private static readonly DateTime Now = TaskTestData.Now;

    /// <summary>RCBU holds branches R-16 and R-21; JCBU holds J-01.</summary>
    private static readonly OrgHierarchy Hierarchy = OrgHierarchy.Build(
        [("RCBU", "CC"), ("JCBU", "WC")],
        [("R-16", "RCBU"), ("R-21", "RCBU"), ("J-01", "JCBU")],
        []);

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IOrgScopeProvider> _scopes = new();
    private readonly Mock<IOrgDirectory> _directory = new();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public TaskHandlerTests()
    {
        _user.SetupGet(u => u.UserName).Returns("tester");
        CallerScope(OrgScopeSet.Unrestricted());

        _scopes.Setup(s => s.GetHierarchyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Hierarchy);
        _directory.Setup(d => d.GetTeamsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OrgTeamInfo>());

        PublishedForm(versionNo: 1);
    }

    public void Dispose() => _db.Dispose();

    private TaskAccess Access => new(_db, _scopes.Object, _user.Object);

    private void CallerScope(OrgScopeSet scope) =>
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope);

    private static OrgScopeSet Branch(string code, string? department = null) =>
        OrgScopeSet.FromRows([new OrgScopeRow(OrgLevels.Branch, code, department)], Hierarchy);

    private void PublishedForm(int versionNo, bool acceptsSubmissions = true) =>
        _forms.Setup(f => f.FindPublishedAsync(TaskTestData.FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(
                TaskTestData.FormId, "FRM", "Form", "نموذج", "SURVEY", "PUBLISHED", versionNo, acceptsSubmissions));

    private async Task<(TaskType Type, FieldTask Task)> SeedAsync(string branch = "R-16")
    {
        var type = TaskTestData.Type();
        var task = TaskTestData.Task(type, branch: branch, number: "TSK-" + branch);

        _db.TaskTypes.Add(type);
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return (type, task);
    }

    private GetTasksQueryHandler ListHandler() =>
        new(_db, Access, _scopes.Object, _directory.Object, _forms.Object, _clock);

    [Fact]
    public async Task List_ShowsOnlyTasksInsideTheCallersTerritory()
    {
        await SeedAsync("R-16");
        await SeedAsync("J-01");
        CallerScope(Branch("R-16"));

        var result = await ListHandler().Handle(new GetTasksQuery(), CancellationToken.None);

        result.Value.Items.Select(t => t.BranchCode).Should().BeEquivalentTo(["R-16"]);
    }

    [Fact]
    public async Task List_ACrewSeesOnlyWorkHandedToItsOwnTeam()
    {
        var crew = Guid.NewGuid();
        var (_, mine) = await SeedAsync("R-16");
        await SeedAsync("R-21");

        var tracked = await _db.Tasks.Include(t => t.Assignments).FirstAsync(t => t.Id == mine.Id);
        tracked.Assign(crew, "supervisor", null, null, null, Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // A crew login: the whole territory, but only its own team's tasks within it.
        _user.SetupGet(u => u.TeamId).Returns(crew);

        var result = await ListHandler().Handle(new GetTasksQuery(), CancellationToken.None);

        result.Value.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task List_FiltersByClusterThroughTheCbusBeneathIt()
    {
        await SeedAsync("R-16");
        var jeddah = TaskTestData.Task(TaskTestData.Type(), number: "TSK-J");
        jeddah.Relocate(new TaskLocation(21.5, 39.2, null, "JCBU", "J-01", null, null), "t", Now);
        _db.TaskTypes.Add(TaskTestData.Type());
        _db.Tasks.Add(jeddah);
        await _db.SaveChangesAsync();

        var result = await ListHandler().Handle(new GetTasksQuery { ClusterCode = "CC" }, CancellationToken.None);

        result.Value.Items.Should().OnlyContain(t => t.CbuCode == "RCBU");
    }

    [Fact]
    public async Task GetById_ATaskOutsideTheTerritoryIsNotFound()
    {
        var (_, task) = await SeedAsync("J-01");
        CallerScope(Branch("R-16"));

        var handler = new GetTaskByIdQueryHandler(_db, Access, _directory.Object, _forms.Object);
        var result = await handler.Handle(new GetTaskByIdQuery(task.Id), CancellationToken.None);

        // Not a 403: whether the task exists at all is not disclosed.
        result.Error.Code.Should().Be(TaskErrors.Codes.TaskNotFound);
    }

    [Fact]
    public async Task Create_PinsTheFormsCurrentVersion_AndTheTypesSla()
    {
        var type = TaskTestData.Type();
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();
        PublishedForm(versionNo: 4);

        var handler = new CreateTaskCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(
            new CreateTaskCommand { TaskTypeId = type.Id, Latitude = 24.66, Longitude = 46.71, CbuCode = "RCBU", BranchCode = "R-16" },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var created = await _db.Tasks.SingleAsync();
        created.FormVersionNo.Should().Be(4);
        created.FillSlaHours.Should().Be(48);
        created.TaskNumber.Should().StartWith("TSK-260921-");
    }

    [Fact]
    public async Task Create_AFormWithNothingPublishedIsRefused()
    {
        var type = TaskTestData.Type();
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();
        PublishedForm(versionNo: 1, acceptsSubmissions: false);

        var handler = new CreateTaskCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(
            new CreateTaskCommand { TaskTypeId = type.Id, Latitude = 24.66, Longitude = 46.71 },
            CancellationToken.None);

        result.Error.Code.Should().Be(TaskErrors.Codes.FormNotPublished);
    }

    [Fact]
    public async Task Create_OutsideTheCallersTerritoryIsRefused()
    {
        var type = TaskTestData.Type();
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();
        CallerScope(Branch("R-16"));

        var handler = new CreateTaskCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(
            new CreateTaskCommand { TaskTypeId = type.Id, Latitude = 21.5, Longitude = 39.2, CbuCode = "JCBU", BranchCode = "J-01" },
            CancellationToken.None);

        result.Error.Code.Should().Be(TaskErrors.Codes.TaskOutsideScope);
        (await _db.Tasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assign_OnlyToATeamWhoseTerritoryCoversTheTask()
    {
        var (_, task) = await SeedAsync("R-16");
        var covering = new OrgTeamInfo(Guid.NewGuid(), "Crew A", null, true);
        var elsewhere = new OrgTeamInfo(Guid.NewGuid(), "Crew J", null, true);

        _directory.Setup(d => d.GetActiveTeamsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new OrgTeamCoverage(covering, Branch("R-16")),
            new OrgTeamCoverage(elsewhere, Branch("J-01")),
        ]);

        var handler = new AssignTaskCommandHandler(_db, Access, _directory.Object, _user.Object, _clock);

        var refused = await handler.Handle(new AssignTaskCommand { TaskId = task.Id, TeamId = elsewhere.Id }, CancellationToken.None);
        refused.Error.Code.Should().Be(TaskErrors.Codes.TeamNotEligible);

        var assigned = await handler.Handle(new AssignTaskCommand { TaskId = task.Id, TeamId = covering.Id }, CancellationToken.None);
        assigned.IsSuccess.Should().BeTrue();

        var stored = await _db.Tasks.Include(t => t.Assignments).SingleAsync(t => t.Id == task.Id);
        stored.ActiveAssignment!.TeamId.Should().Be(covering.Id);

        // No deadline given, so the type's SLA is counted from now: 48h to fill, 24h more to review.
        stored.DueDate.Should().Be(Now.AddHours(48));
        stored.CompletionDueDate.Should().Be(Now.AddHours(72));
    }

    [Fact]
    public async Task Fill_StoresTheAnswersInThePinnedForm_UnderThisTask()
    {
        var (_, task) = await SeedAsync("R-16");
        var submissionId = Guid.NewGuid();
        FormSubmitRequest? sent = null;

        _forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FormSubmitRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(Result.Success(new FormSubmitReceipt(submissionId, 1, IsReplay: false)));

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(
            new SubmitTaskFillCommand { TaskId = task.Id, Answers = new() { ["meter_reading"] = 42 } },
            CancellationToken.None);

        result.Value.Status.Should().Be(TaskStatuses.Submitted);
        sent!.FormId.Should().Be(TaskTestData.FormId);
        sent.VersionNo.Should().Be(task.FormVersionNo);
        sent.ContextType.Should().Be(TasksSchema.FormContextType);
        sent.ContextId.Should().Be(task.Id.ToString("D"));

        (await _db.Tasks.SingleAsync(t => t.Id == task.Id)).LastSubmissionId.Should().Be(submissionId);
    }

    [Fact]
    public async Task Fill_ARetryAfterTheAnswersWereStored_CompletesTheTaskUpdateOnce()
    {
        var (_, task) = await SeedAsync("R-16");
        var submissionId = Guid.NewGuid();

        // First attempt: answers stored, then the task update is recorded.
        _forms.SetupSequence(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FormSubmitReceipt(submissionId, 1, IsReplay: false)))
            // The retry, under the same client key: the form engine answers with the stored row.
            .ReturnsAsync(Result.Success(new FormSubmitReceipt(submissionId, 1, IsReplay: true)));

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var command = new SubmitTaskFillCommand { TaskId = task.Id, ClientSubmissionId = Guid.NewGuid() };

        await handler.Handle(command, CancellationToken.None);
        _db.ChangeTracker.Clear();
        var retry = await handler.Handle(command, CancellationToken.None);

        retry.Value.IsReplay.Should().BeTrue();
        (await _db.Tasks.SingleAsync(t => t.Id == task.Id)).SubmissionCount.Should().Be(1);
    }

    [Fact]
    public async Task Fill_AFormEngineRefusalIsPassedThrough_AndTheTaskIsUntouched()
    {
        var (_, task) = await SeedAsync("R-16");

        _forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<FormSubmitReceipt>(new Error("FormEngine.Submission.AnswersInvalid", "Depth is required.")));

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(new SubmitTaskFillCommand { TaskId = task.Id }, CancellationToken.None);

        result.Error.Message.Should().Contain("Depth");
        (await _db.Tasks.SingleAsync(t => t.Id == task.Id)).Status.Should().Be(TaskStatuses.Created);
    }

    [Fact]
    public async Task Fill_AClosedTaskIsRefusedBeforeAnythingIsStored()
    {
        var (_, task) = await SeedAsync("R-16");
        var tracked = await _db.Tasks.Include(t => t.Assignments).FirstAsync(t => t.Id == task.Id);
        tracked.Expire("admin", null, Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(new SubmitTaskFillCommand { TaskId = task.Id }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Fill_ACrewCannotFillWorkThatMovedToAnotherTeam()
    {
        var crewA = Guid.NewGuid();
        var crewB = Guid.NewGuid();
        var (_, task) = await SeedAsync("R-16");

        var tracked = await _db.Tasks.Include(t => t.Assignments).FirstAsync(t => t.Id == task.Id);
        tracked.Assign(crewA, "supervisor", null, null, null, Now);
        tracked.RecordFill(Guid.NewGuid(), "crew A", Now);
        tracked.Return(TaskReturnReasons.NeedsRevisit, "Revisit", "reviewer", crewB, Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _user.SetupGet(u => u.TeamId).Returns(crewA);

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(new SubmitTaskFillCommand { TaskId = task.Id }, CancellationToken.None);

        // Crew A handed it over, so it no longer even sees the task.
        result.Error.Code.Should().Be(TaskErrors.Codes.TaskNotFound);
    }
}
