using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;
using Workflow.Application.Integrations;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Modules.Workflow;

/// <summary>
/// The designer edits the one SLA rule shared by a Department + FA Type. These cover what it relies on:
/// the context it shows before saving, the domain checks it mirrors, and that rule edits never reach published snapshots.
/// </summary>
public sealed class WorkspaceSlaTests : IDisposable
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly WorkflowDbContext _db;
    private readonly Mock<IWorkflowReferenceData> _references = new();
    private readonly WorkspaceSla _sla;
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly string _store = $"WorkspaceSla_{Guid.NewGuid()}";

    public WorkspaceSlaTests()
    {
        _db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(_store).Options, new StubTenant(_orgId));
        _references.Setup(r => r.IsValidFieldActivityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string department, string field, CancellationToken _) => department.StartsWith('D') && field.StartsWith("FA"));
        _sla = new WorkspaceSla(_db, new StubTenant(_orgId), _references.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Context_ReportsSharedUsage_AndExcludesTheVersionBeingEdited()
    {
        var calendar = Calendar(withPeriods: true);
        (await _sla.SaveAsync(null, Input(calendar.Id), default)).IsSuccess.Should().BeTrue();
        var current = Version("Current", WorkflowVersionStatus.Draft, ("a", "D1", "FA1"), ("b", "D1", "FA1"), ("c", "D1", "FA2"));
        Version("Other draft", WorkflowVersionStatus.Draft, ("x", "D1", "FA1"));
        Version("Published", WorkflowVersionStatus.Published, ("p", "D1", "FA1"));
        Version("Retired", WorkflowVersionStatus.Retired, ("r", "D1", "FA1"));
        Version("Legacy", WorkflowVersionStatus.Draft, false, ("l", "D1", "FA1"));
        await _db.SaveChangesAsync();

        var all = await _sla.ContextAsync("D1", "FA1", null, default);
        all.Rule!.CalendarName.Should().Be("Main calendar");
        all.CalendarActive.Should().BeTrue();
        all.Usage.Should().BeEquivalentTo(new WorkspaceSlaUsage(3, 2, ["Current", "Other draft"], 1));

        var others = await _sla.ContextAsync("D1", "FA1", current, default);
        others.Usage.DraftActivities.Should().Be(1);
        others.Usage.DraftWorkflowNames.Should().Equal("Other draft");
    }

    [Fact]
    public async Task Context_KeepsARuleWhoseCalendarWasDeactivated_AndListsInactiveRules()
    {
        var calendar = Calendar(withPeriods: true);
        var active = (await _sla.SaveAsync(null, Input(calendar.Id), default)).Value;
        (await _sla.SaveAsync(null, Input(calendar.Id) with { Name = "Old", IsActive = false }, default)).IsSuccess.Should().BeTrue();
        calendar.Deactivate(_now);
        await _db.SaveChangesAsync();

        var context = await _sla.ContextAsync("D1", "FA1", null, default);
        context.Rule!.Id.Should().Be(active.Id);
        context.CalendarActive.Should().BeFalse();
        context.InactiveRules.Select(r => r.Name).Should().Equal("Old");
        (await _sla.ResolveAsync("D1", "FA1", default)).Should().BeNull("publication cannot use a rule whose calendar is inactive");
    }

    [Fact]
    public async Task Save_EnforcesTheDomainRulesTheEditorMirrors()
    {
        var calendar = Calendar(withPeriods: false);
        async Task<string?> Error(WorkspaceSlaInput input) => (await _sla.SaveAsync(null, input, default)).Error?.Message;

        (await Error(Input(calendar.Id) with { Duration = 0 })).Should().Contain("positive supported duration");
        (await Error(Input(calendar.Id) with { DurationUnit = (SlaDurationUnit)99 })).Should().Contain("positive supported duration");
        (await Error(Input(calendar.Id) with { DepartmentCode = "X" })).Should().Contain("Field Activity Types");
        (await Error(Input(calendar.Id) with { ReminderMinutes = [0] })).Should().Contain("reminder offsets");
        (await Error(Input(calendar.Id) with { OverdueMinutes = [-1] })).Should().Contain("reminder offsets");
        (await Error(Input(Guid.NewGuid()))).Should().Contain("active calendar");
        (await Error(Input(calendar.Id) with { DurationUnit = SlaDurationUnit.BusinessHours })).Should().Contain("working periods");

        (await _sla.SaveAsync(null, Input(calendar.Id), default)).IsSuccess.Should().BeTrue();
        (await Error(Input(calendar.Id) with { Name = "Second" })).Should().Contain("already exists");
        (await _sla.SaveAsync(null, Input(calendar.Id) with { Name = "Draft rule", IsActive = false }, default)).IsSuccess
            .Should().BeTrue("only one active rule per combination is required; inactive ones may coexist");
    }

    [Fact]
    public async Task Save_UpdatesTheSharedRule_ButNeverAPublishedSnapshot()
    {
        var calendar = Calendar(withPeriods: true);
        var rule = (await _sla.SaveAsync(null, Input(calendar.Id), default)).Value;
        var policy = await _db.SlaPolicies.SingleAsync(p => p.Id == rule.Id);
        var snapshot = JsonSerializer.Serialize(await WorkspaceSla.SnapshotAsync(_db, policy, default), IntegrationJson.Options);
        var published = Version("Published", WorkflowVersionStatus.Published, ("p", "D1", "FA1"));
        var activity = await _db.ActivityDefinitions.SingleAsync(a => a.WorkflowVersionId == published);
        activity.SetPublishedConfiguration(JsonSerializer.Serialize(new { departmentCode = "D1", fieldActivityCode = "FA1", publishedSla = JsonDocument.Parse(snapshot).RootElement }));
        await _db.SaveChangesAsync();

        var updated = await _sla.SaveAsync(rule.Id, Input(calendar.Id) with { Duration = 48, ReminderMinutes = [30] }, default);

        updated.IsSuccess.Should().BeTrue();
        (await _sla.ResolveAsync("D1", "FA1", default))!.Duration.Should().Be(48);
        var stored = WorkspaceDesign.Configuration((await _db.ActivityDefinitions.AsNoTracking().SingleAsync(a => a.Id == activity.Id)).ConfigurationJson)["publishedSla"]!
            .Deserialize<PublishedSla>(IntegrationJson.Options)!;
        stored.Duration.Should().Be(8);
        stored.ReminderMinutes.Should().Equal(60);
    }

    [Fact]
    public async Task Calendars_ReportWhetherBusinessTimeCanBeUsed()
    {
        var withPeriods = Calendar(withPeriods: true, name: "A with periods");
        Calendar(withPeriods: false, name: "B without periods");
        var options = await _sla.CalendarsAsync(default);
        options.Should().BeEquivalentTo([
            new SlaCalendarOption(withPeriods.Id, "A with periods", "UTC", true),
            new SlaCalendarOption(options[1].Id, "B without periods", "UTC", false)]);
    }

    [Fact]
    public async Task CalendarDetails_AddedToALoadedCalendar_AreInsertedNotUpdated()
    {
        // The editor adds working periods and holidays to calendars saved in earlier requests; each request loads the calendar fresh.
        var calendarId = Calendar(withPeriods: false).Id;
        var gate = new Mock<global::Workflow.Application.Abstractions.IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(NWFM.Shared.Results.Result.Success());
        Result<T> Send<T>(Func<global::Workflow.Domain.Repositories.IBusinessCalendarRepository, Task<Result<T>>> handle)
        {
            using var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(_store).Options, new StubTenant(_orgId));
            return handle(new global::Workflow.Infrastructure.Persistence.Repositories.BusinessCalendarRepository(db)).GetAwaiter().GetResult();
        }

        Send(r => new global::Workflow.Application.Commands.AddBusinessCalendarPeriod.AddBusinessCalendarPeriodCommandHandler(gate.Object, r)
            .Handle(new(calendarId, DayOfWeek.Sunday, TimeSpan.FromHours(8), TimeSpan.FromHours(16), true), default)).IsSuccess.Should().BeTrue();
        Send(r => new global::Workflow.Application.Commands.AddBusinessCalendarPeriod.AddBusinessCalendarPeriodCommandHandler(gate.Object, r)
            .Handle(new(calendarId, DayOfWeek.Monday, TimeSpan.FromHours(8), TimeSpan.FromHours(16), true), default)).IsSuccess.Should().BeTrue();
        Send(r => new global::Workflow.Application.Commands.AddBusinessCalendarHoliday.AddBusinessCalendarHolidayCommandHandler(gate.Object, r)
            .Handle(new(calendarId, new DateOnly(2026, 12, 2), "National day", null, true), default)).IsSuccess.Should().BeTrue();

        (await _sla.CalendarsAsync(default)).Single().HasWorkingPeriods.Should().BeTrue();
        var stored = await _db.BusinessCalendars.AsNoTracking().Include(c => c.Periods).Include(c => c.Holidays).SingleAsync(c => c.Id == calendarId);
        stored.Periods.Should().HaveCount(2);
        stored.Holidays.Should().ContainSingle();
    }

    private static WorkspaceSlaInput Input(Guid calendar) => new("Inspection", "D1", "FA1", 8, SlaDurationUnit.Hours, calendar, [60], [0]);

    private BusinessCalendar Calendar(bool withPeriods, string name = "Main calendar")
    {
        var calendar = BusinessCalendar.Create("CAL-" + Guid.NewGuid().ToString("N")[..6], name, "UTC", _now, _orgId);
        if (withPeriods) calendar.AddPeriod(DayOfWeek.Monday, TimeSpan.FromHours(8), TimeSpan.FromHours(16), _now);
        _db.BusinessCalendars.Add(calendar);
        _db.SaveChanges();
        return calendar;
    }

    private Guid Version(string name, WorkflowVersionStatus status, params (string Key, string Department, string Field)[] activities)
        => Version(name, status, true, activities);

    private Guid Version(string name, WorkflowVersionStatus status, bool workspace, params (string Key, string Department, string Field)[] activities)
    {
        var definition = WorkflowDefinition.Create(_orgId, "wf-" + Guid.NewGuid().ToString("N")[..8], name, _now);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), _now);
        if (workspace) version.SetWorkspace("{\"kind\":\"Child\",\"designerVersion\":2}");
        if (status != WorkflowVersionStatus.Draft) version.Publish(Guid.NewGuid(), _now);
        if (status == WorkflowVersionStatus.Retired) version.Retire(_now);
        _db.WorkflowDefinitions.Add(definition);
        _db.WorkflowVersions.Add(version);
        foreach (var (key, department, field) in activities)
            _db.ActivityDefinitions.Add(ActivityDefinition.Create(version.Id, key, ActivityType.UserTask, key, _now,
                configurationJson: JsonSerializer.Serialize(new { departmentCode = department, fieldActivityCode = field })));
        _db.SaveChanges();
        return version.Id;
    }

    private sealed class StubTenant(Guid orgId) : ICurrentTenant
    {
        public Guid OrganizationId => orgId;
    }
}
