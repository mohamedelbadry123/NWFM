namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class ActivityInstanceTests
{
    private static ActivityInstance MakeActive() =>
        ActivityInstance.Start(
            Guid.NewGuid(), Guid.NewGuid(),
            "ReviewTask", ActivityType.UserTask, "Review Document", DateTime.UtcNow);

    [Fact]
    public void Start_SetsStatusToActive()
    {
        var ai = MakeActive();
        ai.Status.Should().Be(ActivityInstanceStatus.Active);
        ai.ActivityNodeKey.Should().Be("ReviewTask");
        ai.ActivityType.Should().Be(ActivityType.UserTask);
        ai.Name.Should().Be("Review Document");
        ai.CompletedAt.Should().BeNull();
        ai.FailedAt.Should().BeNull();
    }

    [Fact]
    public void Complete_SetsStatusAndTimestamp()
    {
        var ai = MakeActive();
        var now = DateTime.UtcNow;
        ai.Complete(now);
        ai.Status.Should().Be(ActivityInstanceStatus.Completed);
        ai.CompletedAt.Should().Be(now);
    }

    [Fact]
    public void Skip_SetsStatusToSkipped()
    {
        var ai = MakeActive();
        ai.Skip(DateTime.UtcNow);
        ai.Status.Should().Be(ActivityInstanceStatus.Skipped);
    }

    [Fact]
    public void Fail_SetsStatusAndReason()
    {
        var ai = MakeActive();
        var now = DateTime.UtcNow;
        ai.Fail("No group mapped", now);
        ai.Status.Should().Be(ActivityInstanceStatus.Failed);
        ai.FailureReason.Should().Be("No group mapped");
        ai.FailedAt.Should().Be(now);
    }
}
