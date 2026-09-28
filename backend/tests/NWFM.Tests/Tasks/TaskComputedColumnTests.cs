namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Application.Common;
using global::Tasks.Application.Tasks.Commands.SubmitTaskFill;
using global::Tasks.Application.Tasks.Models;
using global::Tasks.Application.Tasks.Queries.GetTaskComputedColumns;
using global::Tasks.Application.Tasks.Queries.GetTasks;
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
/// Computed columns in the task grid: worked out by the form engine when a form is filled, kept on
/// the task, shown and sorted on the worklist.
/// </summary>
public sealed class TaskComputedColumnTests : IDisposable
{
    private static readonly DateTime Now = TaskTestData.Now;
    private static readonly Guid FormId = TaskTestData.FormId;

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IOrgScopeProvider> _scopes = new();
    private readonly Mock<IOrgDirectory> _directory = new();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public TaskComputedColumnTests()
    {
        _user.SetupGet(u => u.UserName).Returns("tester");
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.Unrestricted());
        _directory.Setup(d => d.GetTeamsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OrgTeamInfo>());
        _forms.Setup(f => f.FindPublishedAsync(FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(FormId, "FRM", "Form", "نموذج", "SURVEY", "PUBLISHED", 1, true));
    }

    public void Dispose() => _db.Dispose();

    private TaskAccess Access => new(_db, _scopes.Object, _user.Object);

    private void Computes(params FormComputedValue[] values) =>
        _forms.Setup(f => f.ComputeAsync(FormId, It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(values);

    private void Stores(Guid submissionId) =>
        _forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FormSubmitReceipt(submissionId, 1, IsReplay: false)));

    private async Task<FieldTask> SeedAsync(string number = "TSK-1")
    {
        var type = await _db.TaskTypes.Include(t => t.Forms).FirstOrDefaultAsync() ?? TaskTestData.Type();
        if (_db.Entry(type).State == EntityState.Detached)
        {
            _db.TaskTypes.Add(type);
        }

        var task = TaskTestData.Task(type, number: number);
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return task;
    }

    private async Task FillAsync(Guid taskId)
    {
        Stores(Guid.NewGuid());
        var handler = new SubmitTaskFillCommandHandler(_db, Access, _forms.Object, _user.Object, _clock);
        var result = await handler.Handle(new SubmitTaskFillCommand { TaskId = taskId }, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Fill_StoresTheComputedValues_AndTheNextFillReplacesThem()
    {
        var task = await SeedAsync();

        Computes(new("severity", "text", null, "High"), new("area", "number", 4.5m, "4.5"));
        await FillAsync(task.Id);

        var stored = await _db.TaskComputedValues.Where(v => v.FieldTaskId == task.Id).ToListAsync();
        stored.Select(v => (v.Key, v.ValueText, v.ValueNumber)).Should().BeEquivalentTo(
            [("severity", "High", (decimal?)null), ("area", "4.5", 4.5m)]);

        // The form's newer version dropped "area"; its row goes, and "severity" is updated in place.
        Computes(new FormComputedValue("severity", "text", null, "Low"));
        await FillAsync(task.Id);

        var after = await _db.TaskComputedValues.Where(v => v.FieldTaskId == task.Id).ToListAsync();
        after.Should().ContainSingle().Which.Should().Match<TaskComputedValue>(v => v.Key == "severity" && v.ValueText == "Low");
        after.Single().Id.Should().Be(stored.Single(v => v.Key == "severity").Id);
    }

    [Fact]
    public async Task List_CarriesEachTasksCells_AndSortsByAComputedNumber()
    {
        var small = await SeedAsync("TSK-S");
        var large = await SeedAsync("TSK-L");
        var none = await SeedAsync("TSK-N");

        Computes(new FormComputedValue("area", "number", 2m, "2"));
        await FillAsync(small.Id);
        Computes(new FormComputedValue("area", "number", 30m, "30"));
        await FillAsync(large.Id);

        var column = TaskComputedColumnIds.Of(FormId, "area");
        var handler = new GetTasksQueryHandler(_db, Access, _scopes.Object, _directory.Object, _forms.Object, _clock);

        var result = await handler.Handle(
            new GetTasksQuery { SortField = TaskComputedColumnIds.SortPrefix + column, SortDescending = true },
            CancellationToken.None);

        result.Value.Items.Select(t => t.TaskNumber).Should().Equal("TSK-L", "TSK-S", "TSK-N");
        result.Value.Items[0].ComputedValues.Should().ContainSingle().Which.Should().Be(new TaskComputedCellDto(column, "30", 30m));
        result.Value.Items.Single(t => t.Id == none.Id).ComputedValues.Should().BeEmpty();
    }

    [Fact]
    public void SortField_AcceptsAComputedColumn_AndRefusesAMalformedOne()
    {
        var validator = new GetTasksQueryValidator();

        validator.Validate(new GetTasksQuery { SortField = $"computed:{FormId}:area" }).IsValid.Should().BeTrue();
        validator.Validate(new GetTasksQuery { SortField = "computed:not-a-guid:area" }).IsValid.Should().BeFalse();
        validator.Validate(new GetTasksQuery { SortField = "somethingElse" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Columns_ForAType_AreItsFormsColumns_AtTheCurrentVersion()
    {
        var task = await SeedAsync();
        _forms.Setup(f => f.GetComputedColumnsAsync(FormId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FormComputedColumnInfo("severity", "Severity", "الخطورة", "text", true)]);

        var handler = new GetTaskComputedColumnsQueryHandler(_db, _forms.Object);
        var result = await handler.Handle(new GetTaskComputedColumnsQuery(task.TaskTypeId), CancellationToken.None);

        var column = result.Value.Should().ContainSingle().Subject;
        column.Id.Should().Be(TaskComputedColumnIds.Of(FormId, "severity"));
        column.FormCode.Should().Be("FRM");
        column.ShowInTaskGrid.Should().BeTrue();

        var all = await handler.Handle(new GetTaskComputedColumnsQuery(null), CancellationToken.None);
        all.Value.Should().ContainSingle();
    }
}
