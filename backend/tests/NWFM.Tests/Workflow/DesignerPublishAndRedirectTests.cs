namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Moq;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Commands.PublishWorkflowVersion;
using global::Workflow.Application.Constants;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Helpers;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;

public sealed class DesignerPublishAndRedirectTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GroupA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();

    public DesignerPublishAndRedirectTests()
    {
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
    }

    private static string ValidXml() =>
        """
        <Workflow xmlns="https://privora.io/workflow/v1">
          <Activities>
            <Activity nodeKey="start" type="Start" name="Start" />
            <Activity nodeKey="review" type="UserTask" name="Privacy Review" assignmentGroupId="11111111-1111-1111-1111-111111111111" />
            <Activity nodeKey="gw" type="ExclusiveGateway" name="Decision" />
            <Activity nodeKey="end" type="End" name="End" />
          </Activities>
          <Transitions>
            <Transition key="t1" from="start" to="review" />
            <Transition key="t2" from="review" to="gw" />
            <Transition key="t3" from="gw" to="end" condition="OutcomeKey == 'APPROVE'" />
            <Transition key="t4" from="gw" to="end" condition="OutcomeKey == 'REJECT'" />
          </Transitions>
        </Workflow>
        """;

    [Fact]
    public async Task Publish_WhenStatusNotValidated_RevalidatesAndPublishes()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, UserA, DateTime.UtcNow);
        version.UpdateXml(ValidXml(), "hash", DateTime.UtcNow);
        version.ValidationStatus.Should().Be(WorkflowValidationStatus.NotValidated);

        _versions.Setup(r => r.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _versions.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var handler = new PublishWorkflowVersionCommandHandler(
            _gate.Object, _definitions.Object, _versions.Object, new WorkflowXmlCompiler());

        var result = await handler.Handle(new PublishWorkflowVersionCommand(version.Id, UserA), default);

        result.IsSuccess.Should().BeTrue();
        version.Status.Should().Be(WorkflowVersionStatus.Published);
        version.ValidationStatus.Should().Be(WorkflowValidationStatus.Valid);
    }

    [Fact]
    public async Task Complete_RedirectWithoutTarget_Fails()
    {
        var item = WorkItem.Create(OrgA, Guid.NewGuid(), Guid.NewGuid(), GroupA, DateTime.UtcNow);
        item.Claim(UserA, DateTime.UtcNow);

        var workItems = new Mock<IWorkItemRepository>();
        workItems.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var handler = CreateCompleteHandler(workItems.Object, item);

        var result = await handler.Handle(
            new CompleteWorkItemCommand(item.Id, UserA, OrgA, "REDIRECT"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.WorkItem.RedirectTargetRequired);
        item.Status.Should().Be(WorkItemStatus.Claimed);
    }

    [Fact]
    public async Task Complete_RedirectWithGroup_ReassignsToTargetGroup()
    {
        var item = WorkItem.Create(OrgA, Guid.NewGuid(), Guid.NewGuid(), GroupA, DateTime.UtcNow);
        item.Claim(UserA, DateTime.UtcNow);

        var targetGroup = WorkflowAssignmentGroup.Create(
            OrgA, "LEGAL_REVIEW", "Legal", AssignmentStrategy.Manual, DateTime.UtcNow);

        var workItems = new Mock<IWorkItemRepository>();
        workItems.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        workItems.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var groups = new Mock<IWorkflowAssignmentGroupRepository>();
        groups.Setup(r => r.GetByIdAsync(targetGroup.Id, OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetGroup);

        var assembler = new Mock<IWorkItemDtoAssembler>();
        assembler.Setup(a => a.ToDtoAsync(item, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItemDto(
                item.Id, item.WorkflowInstanceId, item.ActivityInstanceId, OrgA, targetGroup.Id,
                targetGroup.Name, null, null, null, null, null, WorkItemStatus.Pending,
                null, null, DateTime.UtcNow, DateTime.UtcNow));

        var handler = CreateCompleteHandler(workItems.Object, item, groups.Object, assembler.Object);

        var result = await handler.Handle(
            new CompleteWorkItemCommand(item.Id, UserA, OrgA, "REDIRECT", RedirectAssignmentGroupId: targetGroup.Id),
            default);

        result.IsSuccess.Should().BeTrue();
        item.Status.Should().Be(WorkItemStatus.Pending);
        item.AssignmentGroupId.Should().Be(targetGroup.Id);
        item.ClaimedByUserId.Should().BeNull();
    }

    [Fact]
    public void OutcomeKeys_Redirect_MatchesVariants()
    {
        WorkflowOutcomeKeys.IsRedirect("REDIRECT").Should().BeTrue();
        WorkflowOutcomeKeys.IsRedirect("redirect").Should().BeTrue();
        WorkflowOutcomeKeys.IsRedirect("APPROVE", "REDIRECT").Should().BeTrue();
        WorkflowOutcomeKeys.IsRedirect("APPROVE").Should().BeFalse();
    }

    private CompleteWorkItemCommandHandler CreateCompleteHandler(
        IWorkItemRepository workItems,
        WorkItem item,
        IWorkflowAssignmentGroupRepository? groups = null,
        IWorkItemDtoAssembler? assembler = null)
    {
        var events = new Mock<IWorkflowEventAppender>();
        events.Setup(e => e.AppendAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<WorkflowEventType>(), It.IsAny<DateTime>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var candidates = new Mock<IWorkflowCandidateFactory>();
        candidates.Setup(c => c.CreateCandidatesAsync(
                OrgA, item.Id, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkItemCandidate>());

        var candidateRepo = new Mock<IWorkItemCandidateRepository>();
        candidateRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<WorkItemCandidate>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CompleteWorkItemCommandHandler(
            _gate.Object,
            workItems,
            Mock.Of<IWorkflowRuntimeEngine>(),
            events.Object,
            assembler ?? Mock.Of<IWorkItemDtoAssembler>(),
            Mock.Of<IWorkflowInstanceRepository>(),
            Mock.Of<IWorkflowVersionRepository>(),
            Mock.Of<IActivityInstanceRepository>(),
            groups ?? Mock.Of<IWorkflowAssignmentGroupRepository>(),
            Mock.Of<IWorkflowDepartmentRepository>(),
            candidates.Object,
            candidateRepo.Object);
    }
}
