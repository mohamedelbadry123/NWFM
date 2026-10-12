namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Moq;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData("valid", null)]
    [InlineData("missing", "ACTIVITY_FORM_REQUIRED")]
    [InlineData("unrelated", "ACTIVITY_FORM_MEMBERSHIP")]
    [InlineData("unavailable", "ACTIVITY_FORM_UNAVAILABLE")]
    [InlineData("missing-version", "ACTIVITY_FORM_UNAVAILABLE")]
    [InlineData("inactive-type", "ACTIVITY_TASK_TYPE")]
    public async Task Publication_ValidatesTheSelectedFormAndPinnedVersion(string scenario, string? expected)
    {
        var type = Guid.NewGuid(); var form = Guid.NewGuid();
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new
        {
            taskTypeId = type,
            formDefinitionId = scenario == "missing" ? (Guid?)null : form,
            formVersionNo = 1
        }), DateTime.UtcNow);
        var repo = new WorkflowVersionRepository(_db);
        var version = (await repo.GetByIdWithProjectionAsync(seeded.Version))!;
        _db.Entry(version).Property(v => v.Status).CurrentValue = WorkflowVersionStatus.Draft;
        var types = new Mock<IWorkflowTaskTypeCatalog>();
        types.Setup(t => t.IsActiveAsync(type, default)).ReturnsAsync(scenario != "inactive-type");
        types.Setup(t => t.FormIdsAsync(type, default)).ReturnsAsync(scenario == "unrelated" ? [] : new[] { form });
        var forms = new Mock<IFormGateway>();
        forms.Setup(f => f.FindPublishedAsync(form, default)).ReturnsAsync(new PublishedFormInfo(form, "FORM", "Form", "", "", "PUBLISHED", 3, scenario != "unavailable"));
        forms.Setup(f => f.GetVersionSchemaAsync(form, 1, default)).ReturnsAsync(scenario == "missing-version" ? null : "{}");
        var publisher = new WorkflowWorkspacePublisher(_db, Mock.Of<IWorkflowReferenceData>(), repo, Mock.Of<IOrgDirectory>(), types.Object, forms.Object);
        var issues = await publisher.ValidateAsync(version, default);
        if (expected is null) issues.Should().NotContain(i => i.Code.StartsWith("ACTIVITY_FORM") || i.Code == "ACTIVITY_TASK_TYPE");
        else issues.Should().Contain(i => i.Code == expected && i.NodeKey == "activity");
        // Existing published versions remain executable even if they predate mandatory form selection.
        _db.Entry(version).Property(v => v.Status).CurrentValue = WorkflowVersionStatus.Published;
        (await publisher.ValidateAsync(version, default)).Should().NotContain(i => i.Code.StartsWith("ACTIVITY_FORM") || i.Code == "ACTIVITY_TASK_TYPE");
    }

    [Theory]
    [InlineData(false, "APPROVE")]
    [InlineData(true, "APPROVE")]
    [InlineData(false, "REJECT")]
    public async Task Completion_UsesPinnedFormAndRecordsItsAnswersOrLeavesTaskOpen(bool invalid, string action)
    {
        var now = DateTime.UtcNow; var formId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var seeded = SeedSingleActivity(ActivityType.UserTask, Config(new { formDefinitionId = formId, formVersionNo = 2 }), now);
        var repo = new WorkflowVersionRepository(_db);
        var version = (await repo.GetByIdWithProjectionAsync(seeded.Version))!;
        // Isolate completion from workspace configuration; published binding is still read from the pinned version.
        version.SetWorkspace(null);
        var instance = WorkflowInstance.Start(_orgId, seeded.Binding.Id, version.Id, "fill", "case", "activity", now);
        var activity = ActivityInstance.Start(_orgId, instance.Id, "activity", ActivityType.UserTask, "Activity", now);
        var item = WorkItem.Create(_orgId, instance.Id, activity.Id, Guid.NewGuid(), now); item.Claim(actor, now);
        _db.WorkflowInstances.Add(instance); _db.ActivityInstances.Add(activity); _db.WorkItems.Add(item); await _db.SaveChangesAsync();
        var forms = new Mock<IFormGateway>();
        forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).ReturnsAsync(invalid
            ? Result.Failure<FormSubmitReceipt>(new Error("Form.Invalid", "Required field missing"))
            : Result.Success(new FormSubmitReceipt(Guid.NewGuid(), 2, true)));
        forms.Setup(f => f.GetLatestByContextAsync(formId, "WorkItem", item.Id.ToString(), default))
            .ReturnsAsync(new FormSubmissionRecord(Guid.NewGuid(), formId, 2, actor.ToString(), "Actor", null, new Dictionary<string, object?> { ["answer"] = "stored" }));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var engine = new Mock<IWorkflowRuntimeEngine>(); engine.Setup(e => e.AdvanceAsync(instance.Id, item.Id, It.IsAny<DateTime>(), default)).ReturnsAsync(Result.Success());
        var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(_db), engine.Object,
            Mock.Of<IWorkflowEventAppender>(), Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(_db), repo,
            new ActivityInstanceRepository(_db), Mock.Of<IWorkflowAssignmentGroupRepository>(), Mock.Of<IWorkflowDepartmentRepository>(),
            Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), forms.Object);
        var result = await handler.Handle(new CompleteWorkItemCommand(item.Id, actor, _orgId, action, FormValues: new() { ["answer"] = JsonSerializer.SerializeToElement("new") }), default);
        if (action == "REJECT") forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Never);
        else forms.Verify(f => f.SubmitAsync(It.Is<FormSubmitRequest>(r => r.FormId == formId && r.VersionNo == 2 && r.ClientSubmissionId == item.Id && r.ContextType == "WorkItem" && r.ContextId == item.Id.ToString()), default), Times.Once);
        if (invalid) { result.IsFailure.Should().BeTrue(); item.Status.Should().Be(WorkItemStatus.Claimed); engine.Verify(e => e.AdvanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never); }
        else { item.Status.Should().Be(WorkItemStatus.Completed); if (action != "REJECT") item.FormDataJson.Should().Contain("stored"); }
    }
}
