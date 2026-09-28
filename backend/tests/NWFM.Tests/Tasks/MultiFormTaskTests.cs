namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Application.Common;
using global::Tasks.Application.Constants;
using global::Tasks.Application.Tasks.Commands.CreateTask;
using global::Tasks.Application.Tasks.Commands.SubmitTaskFill;
using global::Tasks.Application.Tasks.Commands.TaskForms;
using global::Tasks.Domain.Constants;
using global::Tasks.Domain.Entities;
using global::Tasks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using NWFM.Shared.Results;
using NWFM.Tests.Modules.FormEngine;

/// <summary>
/// A task carrying several forms: its type's, plus any added to it. One team fills them all, and the
/// task is filled only once every one is.
/// </summary>
public sealed class MultiFormTaskTests : IDisposable
{
    private static readonly DateTime Now = TaskTestData.Now;
    private static readonly Guid FormA = TaskTestData.FormId;
    private static readonly Guid FormB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FormC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Team = Guid.NewGuid();

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IOrgScopeProvider> _scopes = new();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public MultiFormTaskTests()
    {
        _user.SetupGet(u => u.UserName).Returns("tester");
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.Unrestricted());

        Published(FormA, "FRM-A", 1);
        Published(FormB, "FRM-B", 2);
        Published(FormC, "FRM-C", 3);

        _forms.Setup(f => f.ComputeAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public void Dispose() => _db.Dispose();

    private TaskAccess Access => new(_db, _scopes.Object, _user.Object);

    private void Published(Guid formId, string code, int versionNo, bool accepts = true) =>
        _forms.Setup(f => f.FindPublishedAsync(formId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(formId, code, code, code, "SURVEY", "PUBLISHED", versionNo, accepts));

    private static FieldTask AssignedTask(params Guid[] forms)
    {
        var task = TaskTestData.Task(TaskTestData.Type(forms));
        task.Assign(Team, "supervisor", null, null, null, Now);
        return task;
    }

    // --- The lifecycle ----------------------------------------------------------------------------

    [Fact]
    public void Fill_OneOfSeveralForms_LeavesTheTaskInProgress()
    {
        var task = AssignedTask(FormA, FormB);

        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);

        task.Status.Should().Be(TaskStatuses.InProgress);
        task.ActiveAssignment!.Status.Should().Be(TaskAssignmentStatuses.InProgress);
        task.FilledFormCount.Should().Be(1);
        task.RequiredFormCount.Should().Be(2);
        task.AllRequiredFormsFilled.Should().BeFalse();
        task.History.Last().Note.Should().Contain("1 of 2");
    }

    [Fact]
    public void Fill_TheLastForm_SubmitsTheTask()
    {
        var task = AssignedTask(FormA, FormB);

        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);
        task.RecordFill(FormB, Guid.NewGuid(), "crew", Now.AddMinutes(5));

        task.Status.Should().Be(TaskStatuses.Submitted);
        task.ActiveAssignment!.Status.Should().Be(TaskAssignmentStatuses.Submitted);
        task.SubmissionCount.Should().Be(2);
        task.FormOf(FormB)!.SubmissionCount.Should().Be(1);
    }

    [Fact]
    public void Fill_IsIdempotentPerForm()
    {
        var task = AssignedTask(FormA, FormB);
        var submissionId = Guid.NewGuid();

        task.RecordFill(FormA, submissionId, "crew", Now);
        task.RecordFill(FormA, submissionId, "crew", Now);

        task.SubmissionCount.Should().Be(1);
        task.FormOf(FormA)!.SubmissionCount.Should().Be(1);
    }

    [Fact]
    public void Fill_AFormTheTaskDoesNotCarry_IsRefused()
    {
        var task = AssignedTask(FormA);

        FluentActions.Invoking(() => task.RecordFill(FormB, Guid.NewGuid(), "crew", Now))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Assign_AfterAPartialFill_IsRefused_SoOneTeamFillsEveryForm()
    {
        var task = AssignedTask(FormA, FormB);
        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);

        FluentActions.Invoking(() => task.Assign(Guid.NewGuid(), "supervisor", null, null, null, Now))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Return_ThenRefillOneForm_SubmitsAgain()
    {
        var task = AssignedTask(FormA, FormB);
        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);
        task.RecordFill(FormB, Guid.NewGuid(), "crew", Now);
        task.Return(TaskReturnReasons.IncompleteData, "Fix form B", "reviewer", null, Now);

        task.RecordFill(FormB, Guid.NewGuid(), "crew", Now.AddHours(1));

        task.Status.Should().Be(TaskStatuses.Submitted);
    }

    [Fact]
    public void AttachForm_ToAReturnedTask_MustBeFilledBeforeItSubmitsAgain()
    {
        var task = AssignedTask(FormA);
        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);
        task.Return(TaskReturnReasons.NeedsRevisit, "Also fill the checklist", "reviewer", null, Now);

        var added = task.AttachForm(FormB, 2, "supervisor", Now);
        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now.AddHours(1));

        added.Source.Should().Be(TaskFormSources.Extra);
        added.SortOrder.Should().Be(1);
        task.Status.Should().Be(TaskStatuses.InProgress);

        task.RecordFill(FormB, Guid.NewGuid(), "crew", Now.AddHours(2));
        task.Status.Should().Be(TaskStatuses.Submitted);
    }

    [Fact]
    public void AttachForm_IsRefusedWhileWaitingForReview_AndForAFormAlreadyThere()
    {
        var task = AssignedTask(FormA);

        FluentActions.Invoking(() => task.AttachForm(FormA, 1, "supervisor", Now)).Should().Throw<DomainException>();

        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);
        FluentActions.Invoking(() => task.AttachForm(FormB, 1, "supervisor", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void DetachForm_OnlyAnAddedForm_AndOnlyBeforeAnyFill()
    {
        var task = AssignedTask(FormA);
        task.AttachForm(FormB, 1, "supervisor", Now);
        task.AttachForm(FormC, 1, "supervisor", Now);

        FluentActions.Invoking(() => task.DetachForm(FormA, "supervisor", Now)).Should().Throw<DomainException>();

        task.DetachForm(FormC, "supervisor", Now);
        task.Forms.Select(f => f.FormDefinitionId).Should().BeEquivalentTo([FormA, FormB]);

        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);
        FluentActions.Invoking(() => task.DetachForm(FormB, "supervisor", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void MigrateFormVersion_MovesAnUnfilledForm_EvenAfterAnotherIsFilled()
    {
        var task = AssignedTask(FormA, FormB);
        task.RecordFill(FormA, Guid.NewGuid(), "crew", Now);

        task.MigrateFormVersion(FormB, 5, "admin", Now);

        task.FormOf(FormB)!.FormVersionNo.Should().Be(5);
        FluentActions.Invoking(() => task.MigrateFormVersion(FormA, 5, "admin", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void C2mClosingForm_IsTheTypesFlaggedForm()
    {
        var type = TaskType.Create("T", "T", "T", null, null, [FormA, FormB], FormB, null, null, null, "tester", Now);
        var task = TaskTestData.Task(type);

        task.C2mClosingForm!.FormDefinitionId.Should().Be(FormB);
    }

    // --- The type's forms -------------------------------------------------------------------------

    [Fact]
    public void TaskType_ListsItsFormsInOrder_AndFlagsTheFirstAsClosingByDefault()
    {
        var type = TaskTestData.Type(FormB, FormA);

        type.FormIds.Should().Equal(FormB, FormA);
        type.Forms.Single(f => f.IsC2mClosingForm).FormDefinitionId.Should().Be(FormB);
    }

    [Fact]
    public void TaskType_Update_KeepsTheRowsOfFormsItKeeps()
    {
        var type = TaskTestData.Type(FormA, FormB);
        var rowA = type.Forms.Single(f => f.FormDefinitionId == FormA).Id;

        type.Update("Survey", "مسح", null, null, [FormC, FormA], null, null, 48, 24, "tester", Now);

        type.FormIds.Should().Equal(FormC, FormA);
        type.Forms.Single(f => f.FormDefinitionId == FormA).Id.Should().Be(rowA);
    }

    public static TheoryData<Guid[], Guid?> BadFormLists => new()
    {
        { [], null },
        { [FormA, FormA], null },
        { [FormA], FormB },
        { Enumerable.Range(0, TaskType.MaxForms + 1).Select(_ => Guid.NewGuid()).ToArray(), null },
    };

    [Theory]
    [MemberData(nameof(BadFormLists))]
    public void TaskType_RefusesABadFormList(Guid[] forms, Guid? closing) =>
        FluentActions.Invoking(() => TaskType.Create("T", "T", "T", null, null, forms, closing, null, null, null, "tester", Now))
            .Should().Throw<DomainException>();

    // --- Through the handlers ---------------------------------------------------------------------

    [Fact]
    public async Task Create_PinsTheTypesFormsThenTheExtras_EachAtItsCurrentVersion()
    {
        var type = TaskTestData.Type(FormA, FormB);
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();

        var handler = new CreateTaskCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(
            new CreateTaskCommand
            {
                TaskTypeId = type.Id,
                ExtraFormDefinitionIds = [FormC],
                Latitude = 24.66,
                Longitude = 46.71,
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var created = await _db.Tasks.Include(t => t.Forms).SingleAsync();
        created.OrderedForms.Select(f => (f.FormDefinitionId, f.FormVersionNo, f.Source)).Should().Equal(
            (FormA, 1, TaskFormSources.Type),
            (FormB, 2, TaskFormSources.Type),
            (FormC, 3, TaskFormSources.Extra));
    }

    [Fact]
    public async Task Create_RefusesAnExtraThatIsAlreadyTheTypes_OrThatTakesNoFills()
    {
        var type = TaskTestData.Type(FormA);
        _db.TaskTypes.Add(type);
        await _db.SaveChangesAsync();
        Published(FormC, "FRM-C", 3, accepts: false);

        var handler = new CreateTaskCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);

        var duplicate = await handler.Handle(
            new CreateTaskCommand { TaskTypeId = type.Id, ExtraFormDefinitionIds = [FormA], Latitude = 24.66, Longitude = 46.71 },
            CancellationToken.None);
        var unpublished = await handler.Handle(
            new CreateTaskCommand { TaskTypeId = type.Id, ExtraFormDefinitionIds = [FormC], Latitude = 24.66, Longitude = 46.71 },
            CancellationToken.None);

        duplicate.Error.Code.Should().Be(TaskErrors.Codes.FormAlreadyOnTask);
        unpublished.Error.Code.Should().Be(TaskErrors.Codes.FormNotPublished);
        (await _db.Tasks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Fill_OnAMultiFormTask_MustNameTheForm_AndGoesToThatFormsPinnedVersion()
    {
        var task = TaskTestData.Task(TaskTestData.Type(FormA, FormB));
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        FormSubmitRequest? sent = null;
        _forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FormSubmitRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(Result.Success(new FormSubmitReceipt(Guid.NewGuid(), 1, IsReplay: false)));

        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);

        var unnamed = await handler.Handle(new SubmitTaskFillCommand { TaskId = task.Id }, CancellationToken.None);
        unnamed.Error.Code.Should().Be(TaskErrors.Codes.FormChoiceRequired);
        sent.Should().BeNull();

        var named = await handler.Handle(new SubmitTaskFillCommand { TaskId = task.Id, FormDefinitionId = FormB }, CancellationToken.None);

        named.IsSuccess.Should().BeTrue();
        named.Value.Status.Should().Be(TaskStatuses.InProgress);
        named.Value.FilledFormCount.Should().Be(1);
        named.Value.RequiredFormCount.Should().Be(2);
        sent!.FormId.Should().Be(FormB);
    }

    [Fact]
    public async Task AttachForm_PinsTheFormsCurrentVersion()
    {
        var task = TaskTestData.Task(TaskTestData.Type(FormA));
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var handler = new AttachTaskFormCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(new AttachTaskFormCommand { TaskId = task.Id, FormDefinitionId = FormC }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stored = await _db.TaskForms.SingleAsync(f => f.FieldTaskId == task.Id && f.FormDefinitionId == FormC);
        stored.FormVersionNo.Should().Be(3);
        stored.Source.Should().Be(TaskFormSources.Extra);
    }
}
