namespace NWFM.Tests.Modules.Tasks;

using FluentAssertions;
using global::Tasks.Domain.Constants;
using global::Tasks.Domain.Entities;
using NWFM.Shared.Exceptions;

/// <summary>The task lifecycle: what may happen in which state, and what each step records.</summary>
public sealed class FieldTaskTests
{
    private static readonly DateTime Now = TaskTestData.Now;
    private static readonly Guid TeamA = Guid.NewGuid();
    private static readonly Guid TeamB = Guid.NewGuid();

    private static FieldTask NewTask() => TaskTestData.Task(TaskTestData.Type());

    [Fact]
    public void Create_StartsAsCreated_WithAHistoryRow()
    {
        var task = NewTask();

        task.Status.Should().Be(TaskStatuses.Created);
        task.IsActive.Should().BeTrue();
        task.History.Should().ContainSingle().Which.Should().Match<TaskStatusHistory>(h => h.FromStatus == null && h.ToStatus == TaskStatuses.Created);
    }

    [Theory]
    [InlineData(91, 46)]
    [InlineData(24, 181)]
    public void Create_RefusesAPointThatIsNotOnEarth(double latitude, double longitude)
    {
        var type = TaskTestData.Type();

        var act = () => FieldTask.Create(
            new FieldTaskDraft
            {
                TaskNumber = "TSK-X",
                TaskTypeId = type.Id,
                FormDefinitionId = type.FormDefinitionId,
                FormVersionNo = 1,
                Location = new TaskLocation(latitude, longitude, null, null, null, null, null),
            },
            Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Assign_HandsTheTaskToATeam()
    {
        var task = NewTask();

        task.Assign(TeamA, "supervisor", Now.AddDays(1), Now.AddDays(2), "go", Now);

        task.Status.Should().Be(TaskStatuses.Assigned);
        task.ActiveAssignment!.TeamId.Should().Be(TeamA);
        task.ActiveAssignment.Status.Should().Be(TaskAssignmentStatuses.Pending);
        task.DueDate.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Reassign_SupersedesTheLiveAssignment_RatherThanOverwritingIt()
    {
        var task = NewTask();
        task.Assign(TeamA, "supervisor", null, null, null, Now);

        task.Assign(TeamB, "supervisor", null, null, null, Now.AddHours(1));

        task.Assignments.Should().HaveCount(2);
        task.Assignments.Single(a => a.TeamId == TeamA).Should().Match<TaskAssignment>(a => !a.IsActive && a.Status == TaskAssignmentStatuses.Reassigned);
        task.ActiveAssignment!.TeamId.Should().Be(TeamB);
    }

    [Fact]
    public void Assign_IsRefusedOnceTheTaskIsFilled()
    {
        var task = NewTask();
        task.Assign(TeamA, "supervisor", null, null, null, Now);
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        var act = () => task.Assign(TeamB, "supervisor", null, null, null, Now);

        act.Should().Throw<DomainException>().WithMessage("*before it is filled*");
    }

    [Fact]
    public void RecordFill_MovesTheTaskAndItsAssignmentToSubmitted()
    {
        var task = NewTask();
        task.Assign(TeamA, "supervisor", null, null, null, Now);
        var submissionId = Guid.NewGuid();

        task.RecordFill(submissionId, "crew", Now);

        task.Status.Should().Be(TaskStatuses.Submitted);
        task.SubmissionCount.Should().Be(1);
        task.LastSubmissionId.Should().Be(submissionId);
        task.ActiveAssignment!.Status.Should().Be(TaskAssignmentStatuses.Submitted);
    }

    [Fact]
    public void RecordFill_ForTheSameSubmissionTwice_CountsItOnce()
    {
        // A client retrying a fill whose first attempt stored the answers but lost the reply.
        var task = NewTask();
        var submissionId = Guid.NewGuid();

        task.RecordFill(submissionId, "crew", Now);
        task.RecordFill(submissionId, "crew", Now);

        task.SubmissionCount.Should().Be(1);
        task.History.Count(h => h.ToStatus == TaskStatuses.Submitted).Should().Be(1);
    }

    [Fact]
    public void RecordFill_ASecondFillBeforeReview_KeepsTheStatusAndLogsIt()
    {
        var task = NewTask();
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        task.RecordFill(Guid.NewGuid(), "crew", Now.AddMinutes(5));

        task.Status.Should().Be(TaskStatuses.Submitted);
        task.SubmissionCount.Should().Be(2);
        task.History.Last().Should().Match<TaskStatusHistory>(h => h.FromStatus == TaskStatuses.Submitted && h.ToStatus == TaskStatuses.Submitted);
    }

    [Fact]
    public void Complete_OnlyFromSubmitted()
    {
        var task = NewTask();

        var early = () => task.Complete("reviewer", null, Now);
        early.Should().Throw<DomainException>();

        task.RecordFill(Guid.NewGuid(), "crew", Now);
        task.Complete("reviewer", "ok", Now);

        task.Status.Should().Be(TaskStatuses.Approved);
        task.CompletedBy.Should().Be("reviewer");
    }

    [Fact]
    public void Return_WithAReassignment_HandsTheReworkToTheOtherTeam()
    {
        var task = NewTask();
        task.Assign(TeamA, "supervisor", null, null, null, Now);
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        task.Return(TaskReturnReasons.WrongLocation, "Wrong street", "reviewer", TeamB, Now);

        task.Status.Should().Be(TaskStatuses.Returned);
        task.ReturnCount.Should().Be(1);
        task.ActiveAssignment!.TeamId.Should().Be(TeamB);
        task.Assignments.Single(a => a.TeamId == TeamA).Status.Should().Be(TaskAssignmentStatuses.Reassigned);
    }

    [Fact]
    public void Return_ThenRefill_GoesBackToSubmitted()
    {
        var task = NewTask();
        task.RecordFill(Guid.NewGuid(), "crew", Now);
        task.Return(TaskReturnReasons.IncompleteData, "Missing photos", "reviewer", null, Now);

        task.RecordFill(Guid.NewGuid(), "crew", Now.AddHours(1));

        task.Status.Should().Be(TaskStatuses.Submitted);
    }

    [Fact]
    public void Return_RefusesAnUnknownReason()
    {
        var task = NewTask();
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        var act = () => task.Return("BECAUSE", "no", "reviewer", null, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Expire_ClosesTheTask_AndNothingMoreCanBeRecorded()
    {
        var task = NewTask();
        task.Expire("admin", "duplicate", Now);

        task.Status.Should().Be(TaskStatuses.Expired);
        task.IsActive.Should().BeFalse();

        var fill = () => task.RecordFill(Guid.NewGuid(), "crew", Now);
        fill.Should().Throw<DomainException>();
    }

    [Fact]
    public void Relocate_IsRefusedOnceTheTaskIsFilled()
    {
        var task = NewTask();
        task.RecordFill(Guid.NewGuid(), "crew", Now);

        var act = () => task.Relocate(new TaskLocation(24.7, 46.7, null, "RCBU", "R-21", null, null), "admin", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MigrateFormVersion_MovesForwardOnly_WhileUnfilled()
    {
        var task = NewTask();

        task.MigrateFormVersion(3, "admin", Now);
        task.FormVersionNo.Should().Be(3);

        var backwards = () => task.MigrateFormVersion(2, "admin", Now);
        backwards.Should().Throw<DomainException>();

        task.RecordFill(Guid.NewGuid(), "crew", Now);
        var afterFill = () => task.MigrateFormVersion(4, "admin", Now);
        afterFill.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deadlines_ReviewCannotBeDueBeforeTheFill()
    {
        var task = NewTask();

        var act = () => task.UpdateDetails(null, null, TaskPriorities.Normal, null, Now.AddDays(2), Now.AddDays(1), "admin", Now);

        act.Should().Throw<DomainException>();
    }
}
