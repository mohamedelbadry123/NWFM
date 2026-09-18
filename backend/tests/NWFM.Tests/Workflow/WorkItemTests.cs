namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkItemTests
{
    private static WorkItem MakePending() =>
        WorkItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void Create_SetsStatusToPending()
    {
        var item = MakePending();
        item.Status.Should().Be(WorkItemStatus.Pending);
        item.ClaimedByUserId.Should().BeNull();
        item.CompletedByUserId.Should().BeNull();
    }

    [Fact]
    public void Claim_SetsClaimedUserAndStatus()
    {
        var item = MakePending();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        item.Claim(userId, now);
        item.Status.Should().Be(WorkItemStatus.Claimed);
        item.ClaimedByUserId.Should().Be(userId);
        item.ClaimedAt.Should().Be(now);
    }

    [Fact]
    public void Claim_WhenNotPending_ThrowsInvalidOperationException()
    {
        var item = MakePending();
        item.Claim(Guid.NewGuid(), DateTime.UtcNow);

        var act = () => item.Claim(Guid.NewGuid(), DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Pending*");
    }

    [Fact]
    public void Release_WhenNotClaimed_ThrowsInvalidOperationException()
    {
        var item = MakePending();

        var act = () => item.Release(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Claimed*");
    }

    [Fact]
    public void Complete_WhenNotClaimed_ThrowsInvalidOperationException()
    {
        var item = MakePending();

        var act = () => item.Complete(Guid.NewGuid(), "Approve", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Claimed*");
    }

    [Fact]
    public void Release_ClearsClaimAndReturnsToPending()
    {
        var item = MakePending();
        item.Claim(Guid.NewGuid(), DateTime.UtcNow);
        item.Release(DateTime.UtcNow.AddSeconds(1));
        item.Status.Should().Be(WorkItemStatus.Pending);
        item.ClaimedByUserId.Should().BeNull();
        item.ClaimedAt.Should().BeNull();
    }

    [Fact]
    public void Complete_SetsCompletedFields()
    {
        var item = MakePending();
        var userId = Guid.NewGuid();
        item.Claim(userId, DateTime.UtcNow);
        var now = DateTime.UtcNow.AddSeconds(5);
        item.Complete(userId, "Approve", now, "Looks good");
        item.Status.Should().Be(WorkItemStatus.Completed);
        item.CompletedByUserId.Should().Be(userId);
        item.ActionTaken.Should().Be("Approve");
        item.CommentText.Should().Be("Looks good");
        item.CompletedAt.Should().Be(now);
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var item = MakePending();
        item.Cancel(DateTime.UtcNow);
        item.Status.Should().Be(WorkItemStatus.Cancelled);
    }

    [Fact]
    public void Create_WithDueAt_StoresDueAt()
    {
        var due = DateTime.UtcNow.AddDays(3);
        var item = WorkItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), DateTime.UtcNow, due);
        item.DueAt.Should().Be(due);
    }
}
