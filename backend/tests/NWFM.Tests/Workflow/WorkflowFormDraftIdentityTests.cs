namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Mapping;
using global::Workflow.Application.Queries.GetWorkItemById;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Moq;
using NWFM.Shared.Results;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FormDraftIdentityDetailIsStableAcrossClaimAndSeparatesOwnershipAttempts(bool multi, bool legacy)
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var recipient = Guid.NewGuid(); var form = Guid.NewGuid();
        var config = new BusinessActivityConfiguration
        {
            FormDefinitionId = multi ? null : form,
            FormVersionNo = multi ? null : 2,
            FormSteps = multi ? [new() { Id = "first", FormId = form, VersionNo = 2 }, new() { Id = "second", FormId = Guid.NewGuid(), VersionNo = 1 }] : []
        };
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(config), now);
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version, "draft-identity", "order", "activity", now);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.UserTask, "Step", now);
        var item = WorkItem.Create(_orgId, root.Id, activity.Id, Guid.NewGuid(), now);
        if (multi) item.SetFormProgress(JsonSerializer.Serialize(ActivityFormProgress.Start(config.FormSteps, item.Id)));
        item.SetFormData("{\"server\":42}"); _db.AddRange(root, activity, item);
        if (legacy) _db.Entry(item).Property(i => i.FormReceiptAttempt).CurrentValue = null;
        await _db.SaveChangesAsync();
        var assembler = new WorkItemDtoAssembler(new WorkflowRequestRepository(_db), new WorkflowAssignmentGroupRepository(_db), new WorkflowInstanceRepository(_db),
            new ActivityInstanceRepository(_db), new WorkflowVersionRepository(_db));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
        var handler = new GetWorkItemByIdQueryHandler(gate.Object, new WorkItemRepository(_db), assembler);
        async Task<WorkItemDto> Detail() => (await handler.Handle(new(item.Id, _orgId), default)).Value;
        var pending = await Detail(); pending.FormDraftKey.Should().NotBeNull(); pending.FormDraftKey.Should().NotBe(Guid.Empty); pending.FormReceiptKey.Should().BeNull();
        pending.FormDraftKey.Should().NotBe(item.Id); pending.FormDraftKey.Should().NotBe(item.FormReceiptAttempt ?? Guid.Empty);
        var progress = item.FormProgressJson; var beforeReceipt = item.FormReceiptKey(item.Id, actor);
        item.Claim(actor, now.AddSeconds(1)); await _db.SaveChangesAsync(); await _db.Entry(item).ReloadAsync();
        var claimed = await Detail(); claimed.FormDraftKey.Should().Be(pending.FormDraftKey); claimed.FormReceiptKey.Should().Be(beforeReceipt);
        claimed.FormDraftKey.Should().NotBe(claimed.FormReceiptKey!.Value);
        if (legacy) claimed.FormReceiptKey.Should().Be(item.Id);
        item.SetDueAt(now.AddDays(1), now.AddSeconds(2)); item.Delegate(actor, now.AddSeconds(3)); await _db.SaveChangesAsync();
        (await Detail()).FormDraftKey.Should().Be(claimed.FormDraftKey); item.FormReceiptKey(item.Id, actor).Should().Be(beforeReceipt);
        item.Delegate(recipient, now.AddSeconds(4)); await _db.SaveChangesAsync();
        var delegated = await Detail(); delegated.FormDraftKey.Should().NotBe(claimed.FormDraftKey!.Value);
        item.Release(now.AddSeconds(5)); await _db.SaveChangesAsync(); var unclaimed = await Detail();
        unclaimed.FormReceiptKey.Should().BeNull(); unclaimed.FormDraftKey.Should().NotBe(delegated.FormDraftKey!.Value);
        // The original view is absent for this whole claim/release cycle: both observed receipts are null.
        item.Claim(recipient, now.AddSeconds(6)); item.Release(now.AddSeconds(7)); await _db.SaveChangesAsync();
        var returned = await Detail(); returned.FormReceiptKey.Should().BeNull(); returned.FormDraftKey.Should().NotBe(unclaimed.FormDraftKey!.Value);
        item.Reassign(Guid.NewGuid(), now.AddSeconds(8)); await _db.SaveChangesAsync();
        var reassigned = await Detail(); reassigned.FormDraftKey.Should().NotBe(returned.FormDraftKey!.Value);
        item.Claim(actor, now.AddSeconds(9)); await _db.SaveChangesAsync();
        (await Detail()).FormDraftKey.Should().Be(reassigned.FormDraftKey); item.FormProgressJson.Should().Be(progress); item.FormDataJson.Should().Be("{\"server\":42}");
        var stableReceipt = item.FormReceiptKey(item.Id, actor);
        var another = WorkItem.Create(_orgId, root.Id, Guid.NewGuid(), item.AssignmentGroupId, now);
        another.FormDraftKey(item.Id).Should().NotBe(item.FormDraftKey(item.Id), "work item identity participates even for the same visit argument");
        var nextVisit = Guid.NewGuid(); item.FormDraftKey(nextVisit).Should().NotBe(item.FormDraftKey(item.Id));
        if (multi)
        {
            item.SetFormProgress(JsonSerializer.Serialize(new ActivityFormProgress { ActiveStepId = "second", VisitId = nextVisit })); await _db.SaveChangesAsync();
            var next = await Detail(); next.FormDraftKey.Should().Be(item.FormDraftKey(nextVisit)); next.FormVisitId.Should().Be(nextVisit);
            next.SelectedForm!.FormId.Should().Be(config.FormSteps[1].FormId);
        }
        item.FormReceiptKey(item.Id, actor).Should().Be(stableReceipt, "deriving draft identities never mutates submission identities");
        var detail = await Detail(); var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        JsonSerializer.Deserialize<JsonElement>(json).GetProperty("formDraftKey").GetGuid().Should().Be(detail.FormDraftKey!.Value);
        json.Should().NotContain("formReceiptAttempt").And.NotContain(item.FormReceiptAttempt!.Value.ToString("D")).And.NotContain(item.FormReceiptAttempt.Value.ToString("N"));
        JsonSerializer.Serialize(item).Should().NotContain("FormReceiptAttempt");
        (await handler.Handle(new(item.Id, Guid.NewGuid()), default)).IsFailure.Should().BeTrue();
        // A draft continuity key cannot substitute for the actor-owned receipt even with correct form pins.
        var visit = multi ? nextVisit : item.Id; var pin = multi ? config.FormSteps[1] : new ActivityFormStep { FormId = form, VersionNo = 2 };
        (await new WorkflowFormSubmissionGuard(_db).ValidateAsync(pin.FormId, pin.VersionNo, "WorkItem", multi ? item.Id + ":" + visit : item.Id.ToString(),
            detail.FormDraftKey, new Dictionary<string, object?>(), actor.ToString(), default)).IsFailure.Should().BeTrue();
        (await new WorkflowFormSubmissionGuard(_db).ValidateAsync(pin.FormId, pin.VersionNo, "WorkItem", multi ? item.Id + ":" + visit : item.Id.ToString(),
            item.FormReceiptKey(visit, actor), new Dictionary<string, object?>(), actor.ToString(), default)).IsSuccess.Should().BeTrue();
    }
}
