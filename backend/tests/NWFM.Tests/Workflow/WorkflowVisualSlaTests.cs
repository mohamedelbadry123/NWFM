using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Workflow.Application.Workspace;
using Workflow.Domain.Enums;

namespace NWFM.Tests.Modules.Workflow;

public sealed class WorkflowVisualSlaTests
{
    private static PublishedSla Policy(int duration, SlaDurationUnit unit) => new(Guid.NewGuid(), "Isolation", duration, unit, "UTC",
        [new(DayOfWeek.Monday, TimeSpan.FromHours(8), TimeSpan.FromHours(12)), new(DayOfWeek.Monday, TimeSpan.FromHours(13), TimeSpan.FromHours(17)),
         new(DayOfWeek.Tuesday, TimeSpan.FromHours(8), TimeSpan.FromHours(17)), new(DayOfWeek.Wednesday, TimeSpan.FromHours(8), TimeSpan.FromHours(17))], [], [60, 15], [0, 60]);
    [Fact]
    public void BusinessHours_UsesSplitPeriods_AndSkipsHolidays()
    {
        var policy = Policy(7, SlaDurationUnit.BusinessHours) with { Holidays = [new(new DateOnly(2026, 9, 22), false)] };
        PublishedSlaClock.Deadline(policy, new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc));
    }
    [Fact]
    public void Snapshot_IsIndependentOfSubsequentPolicyEdits()
    {
        var policy = Policy(8, SlaDurationUnit.Hours);
        var published = JsonSerializer.Deserialize<PublishedSla>(JsonSerializer.Serialize(policy))!;
        policy = policy with { Duration = 100, TimeZone = "Asia/Riyadh" };
        var start = DateTime.UtcNow;
        PublishedSlaClock.Deadline(published, start).Should().Be(start.AddHours(8));
    }
    [Fact]
    public void AlertSchedule_HasStableDistinctOccurrenceKeys_AndOneDeadlineBreach()
    {
        var start = DateTime.UtcNow;
        var schedule = PublishedSlaClock.Alerts(Policy(2, SlaDurationUnit.Hours) with { OverdueMinutes = [0, 0, 60], ReminderMinutes = [15, 15, 180] }, start);
        schedule.Should().HaveCount(4);
        schedule.Select(a => a.Key).Should().OnlyHaveUniqueItems();
        schedule[0].At.Should().Be(start);
        schedule.Single(a => a.Key == "overdue:0").At.Should().Be(start.AddHours(2));
    }
    [Fact]
    public void DraftConversion_IsIdempotent_PreservesHiddenData_AndKeepsEventsOutOfSequence()
    {
        var config = new { formFields = new[] { new { key = "future-form" } }, events = new[] { new { id = "mail", kind = "Email", name = "Notify", trigger = "OnComment", required = false, configuration = new { to = "demo@example.test" } } } };
        var xml = new XElement("Workflow", new XAttribute("workspaceJson", "{\"kind\":\"Child\",\"designerVersion\":2}"),
            new XElement("Activities", new XElement("Activity", new XAttribute("nodeKey", "review"), new XAttribute("type", "UserTask"), new XAttribute("configurationJson", JsonSerializer.Serialize(config)))), new XElement("Transitions"));
        var converted = WorkspaceDesign.NormalizeDraft(xml.ToString());
        WorkspaceDesign.NormalizeDraft(converted).Should().Be(converted);
        var doc = XDocument.Parse(converted);
        doc.Descendants("Activity").Should().HaveCount(2);
        doc.Descendants("Transition").Should().BeEmpty();
        var eventConfig = doc.Descendants("Activity").Last().Attribute("configurationJson")!.Value;
        WorkspaceDesign.Binding(eventConfig).Should().Be(new EventTriggerBinding("review", "OnComment"));
        WorkspaceDesign.Configuration(doc.Descendants("Activity").First().Attribute("configurationJson")!.Value)["formFields"]!.ToJsonString().Should().Contain("future-form");
    }
    [Fact]
    public void LegacyDraft_IsNotRewritten()
    {
        const string xml = "<Workflow workspaceJson='{\"kind\":\"Child\"}'><Activities/></Workflow>";
        WorkspaceDesign.NormalizeDraft(xml).Should().Be(xml);
    }
}
