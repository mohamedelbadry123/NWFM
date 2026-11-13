namespace NWFM.Tests.Modules.Workflow;

using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Forms.Common;
using global::FormEngine.Application.Submissions.Common;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::FormEngine.Infrastructure.Persistence;
using global::FormEngine.Infrastructure.Submissions;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ClaimWorkItem;
using global::Workflow.Application.Commands.CompleteWorkItem;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Tests.Infrastructure;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [MappingSqlFact]
    public async Task ReceiptSqlMigrationBackfillsLegacyKeysAndConcurrentOwnershipRemainsAtomic()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        WorkflowDbContext Context() => new(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(sql.ConnectionString,
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).Options, new StubTenant(_orgId));
        var itemId = Guid.NewGuid(); var visitId = Guid.NewGuid(); var oldVisit = Guid.NewGuid(); var actor = Guid.NewGuid(); var now = DateTime.UtcNow;
        var progress = JsonSerializer.Serialize(new ActivityFormProgress { VisitId = visitId, ActiveStepId = "second", Visits = [new() { VisitId = oldVisit, ActorId = actor, SubmissionId = Guid.NewGuid() }] });
        await using (var db = Context())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261110232446_MappingStatusEligibility");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [Workflow].[work_items] ([Id],[OrganizationId],[WorkflowInstanceId],[ActivityInstanceId],[AssignmentGroupId],[ClaimedByUserId],[ClaimedAt],[Status],[CreatedAt],[UpdatedAt],[FormProgressJson]) VALUES ({itemId},{_orgId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{actor},{now},N'Claimed',{now},{now},{progress})");
            await db.Database.MigrateAsync();
            var item = await db.WorkItems.SingleAsync(); item.FormProgressJson.Should().Be(progress); item.FormReceiptKey(visitId, actor).Should().Be(visitId);
            (await db.FormReceiptReservations.Select(r => r.ReceiptKey).ToListAsync()).Should().BeEquivalentTo(new[] { itemId, visitId, oldVisit });
            await db.GetService<IMigrator>().MigrateAsync("20261110232446_MappingStatusEligibility");
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            (await db.WorkItems.SingleAsync()).FormProgressJson.Should().Be(progress);
            (await db.FormReceiptReservations.Select(r => r.ReceiptKey).ToListAsync()).Should().BeEquivalentTo(new[] { itemId, visitId, oldVisit });
        }
        await using var left = Context(); await using var right = Context();
        var a = await left.WorkItems.SingleAsync(); var b = await right.WorkItems.SingleAsync(); var next = Guid.NewGuid();
        a.Delegate(next, now.AddSeconds(1)); b.Delegate(next, now.AddSeconds(1));
        var keyA = a.FormReceiptKey(visitId, next); var keyB = b.FormReceiptKey(visitId, next); keyA.Should().NotBe(keyB);
        async Task<bool> Save(WorkflowDbContext db) { try { await db.SaveChangesAsync(); return true; } catch (DbUpdateConcurrencyException) { return false; } }
        var saves = await Task.WhenAll(Save(left), Save(right)); saves.Count(x => x).Should().Be(1);
        await using var verify = Context(); var winner = await verify.WorkItems.SingleAsync();
        winner.FormProgressJson.Should().Be(progress); winner.ClaimedByUserId.Should().Be(next);
        var key = winner.FormReceiptKey(visitId, next); key.Should().Be(saves[0] ? keyA : keyB);
        (await verify.FormReceiptReservations.Select(r => r.ReceiptKey).ToListAsync()).Should().BeEquivalentTo(new[] { itemId, visitId, oldVisit, key });
        // Reservation is global, while the WorkItem authorization lookup remains tenant-scoped.
        await using var otherTenant = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlServer(sql.ConnectionString).Options, new StubTenant(Guid.NewGuid()));
        (await new WorkflowFormSubmissionGuard(otherTenant).ValidateAsync(Guid.NewGuid(), 1, "Standalone", null, key, new Dictionary<string, object?>(), next.ToString(), default)).IsFailure.Should().BeTrue();
        (await otherTenant.FormReceiptReservations.CountAsync()).Should().Be(0);
    }

    private sealed class ReceiptCommitFault : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<WorkItem>().Any(e => e.Entity.FormDataJson is not null || e.Entity.FormProgressJson?.Contains("SubmissionId", StringComparison.Ordinal) == true))
            { Fired = true; throw new IOException("Receipt committed; workflow commit lost."); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReceiptQueries : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }

    [MappingSqlFact]
    public async Task ReceiptSqlActualFormCommitSurvivesWorkflowRollbackAndOwnershipChanges()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        WorkflowDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(sql.ConnectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).AddInterceptors(interceptors).Options, new StubTenant(_orgId));
        await using var forms = new FormEngineDbContext(new DbContextOptionsBuilder<FormEngineDbContext>().UseSqlServer(sql.ConnectionString,
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", "FE")).Options);
        await forms.Database.MigrateAsync();
        using var statements = new SqlStatementStore(NullLogger<SqlStatementStore>.Instance);
        var store = new FormSubmissionStore(forms, statements);
        var publisher = new FormPublisher(forms, store, Mock.Of<NWFM.Shared.Caching.ICacheService>(), TimeProvider.System);
        var now = DateTime.UtcNow;
        var form = FormDefinition.Create("RECEIPTS", "Receipts", "Receipts", FormCategories.Inspection, null, null, "test", now);
        form.SetSchema(global::NWFM.Tests.Modules.FormEngine.FormEngineTestData.SimpleSchema(), null, null, "test", now);
        forms.Add(form); await forms.SaveChangesAsync(); (await publisher.PublishAsync(form.Id, "test", default)).IsSuccess.Should().BeTrue();
        await using (var db = Context()) await db.Database.MigrateAsync();
        _candidateFactory.Setup(c => c.CreateCandidatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<WorkItemCandidate>());
        foreach (var multi in new[] { false, true })
            foreach (var transfer in new[] { "Delegate", "Release", "Reassign" })
            {
                var actorA = Guid.NewGuid(); var actorB = Guid.NewGuid(); var admin = Guid.NewGuid(); var group = Guid.NewGuid();
                var config = new BusinessActivityConfiguration
                {
                    SlaDurationHours = 24,
                    FormDefinitionId = multi ? null : form.Id,
                    FormVersionNo = multi ? null : 1,
                    FormSteps = multi ? [new() { Id = "first", FormId = form.Id, VersionNo = 1 }, new() { Id = "second", FormId = form.Id, VersionNo = 1 }] : []
                };
                Guid itemId, rootId, keyA, keyB;
                await using (var db = Context())
                {
                    var definition = WorkflowDefinition.Create(_orgId, Guid.NewGuid().ToString(), "Receipts", now);
                    var version = WorkflowVersion.CreateDraft(definition.Id, 1, actorA, now); version.Publish(actorA, now);
                    var start = ActivityDefinition.Create(version.Id, "start", ActivityType.Start, "Start", now);
                    var step = ActivityDefinition.Create(version.Id, "step", ActivityType.UserTask, "Step", now, configurationJson: Config(config));
                    var end = ActivityDefinition.Create(version.Id, "end", ActivityType.End, "End", now);
                    var binding = WorkflowBinding.Create(definition.Id, _orgId, "Test", "Receipt", Guid.NewGuid().ToString(), now, mode: WorkflowBindingMode.Active);
                    db.AddRange(definition, version, start, step, end, binding,
                        WorkflowTransition.Create(version.Id, start.Id, step.Id, "enter", 1, now), WorkflowTransition.Create(version.Id, step.Id, end.Id, "exit", 1, now));
                    await db.SaveChangesAsync();
                    _assignmentResolver.Setup(a => a.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(group));
                    var started = await BuildEngine(db).StartAsync(_orgId, binding.Id, "receipt", Guid.NewGuid().ToString(), now, pinnedWorkflowVersionId: version.Id);
                    started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : ""); rootId = started.Value.Id;
                    db.AddRange(WorkflowParticipant.Create(_orgId, actorA, "A", actorA + "@example.test", now), WorkflowParticipant.Create(_orgId, actorB, "B", actorB + "@example.test", now));
                    var item = await db.WorkItems.SingleAsync(w => w.WorkflowInstanceId == rootId);
                    if (transfer != "Release") item.Claim(actorA, now);
                    await db.SaveChangesAsync();
                    itemId = item.Id; keyA = item.FormReceiptKey(itemId, actorA);
                }
                var attribution = new TrustedFormSubmissionActor();
                (FormSubmissionService Service, IFormGateway Gateway) Forms(WorkflowDbContext db, Guid actor)
                {
                    var user = new Mock<NWFM.Shared.Abstractions.ICurrentUser>(); user.SetupGet(u => u.Id).Returns(actor.ToString());
                    var service = new FormSubmissionService(forms, store, publisher, Mock.Of<NWFM.Shared.Storage.IFileStorage>(), user.Object, TimeProvider.System,
                        Options.Create(new NWFM.Shared.Options.FileStorageOptions()), Options.Create(new global::FormEngine.Domain.Options.FormEngineOptions()), [new WorkflowFormSubmissionGuard(db)], attribution);
                    return (service, new global::FormEngine.Infrastructure.Integration.FormGateway(forms, store, service, Mock.Of<NWFM.Shared.Storage.IFileStorage>()));
                }
                async Task Complete(WorkflowDbContext db, Guid actor, int answer, Guid? visit)
                {
                    var f = Forms(db, transfer == "Delegate" ? admin : actor);
                    using var demo = transfer == "Delegate" ? attribution.BeginAuthorizedDemo(itemId, admin, actor, "Participant") : null;
                    var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
                    var groups = new Mock<IWorkflowAssignmentGroupRepository>(); groups.Setup(g => g.IsUserMemberOfGroupAsync(group, actor, _orgId, default)).ReturnsAsync(true);
                    var handler = new CompleteWorkItemCommandHandler(gate.Object, new WorkItemRepository(db), BuildEngine(db), new WorkflowEventAppender(new WorkflowEventRepository(db)),
                        Mock.Of<IWorkItemDtoAssembler>(), new WorkflowInstanceRepository(db), new WorkflowVersionRepository(db), new ActivityInstanceRepository(db), Mock.Of<IWorkflowAssignmentGroupRepository>(),
                        Mock.Of<IWorkflowDepartmentRepository>(), Mock.Of<IWorkflowCandidateFactory>(), Mock.Of<IWorkItemCandidateRepository>(), f.Gateway);
                    var command = new CompleteWorkItemCommand(itemId, actor, _orgId, "APPROVE", FormValues: new() { ["meter_reading"] = JsonSerializer.SerializeToElement(answer) }, FormVisitId: visit);
                    var pipeline = new WorkflowRuntimeCommandLock<CompleteWorkItemCommand, Result<global::Workflow.Application.DTOs.WorkItemDto>>(db);
                    var result = await pipeline.Handle(command, async () =>
                    {
                        if ((await db.WorkItems.SingleAsync(w => w.Id == itemId)).Status == WorkItemStatus.Pending)
                        {
                            var claim = new ClaimWorkItemCommandHandler(gate.Object, new WorkItemRepository(db), groups.Object, new WorkflowParticipantRepository(db),
                                new WorkflowEventAppender(new WorkflowEventRepository(db)), Mock.Of<IWorkItemDtoAssembler>(), Mock.Of<IWorkflowRequestProjector>());
                            (await claim.Handle(new(itemId, actor, _orgId), default)).IsSuccess.Should().BeTrue();
                        }
                        return await handler.Handle(command, default);
                    }, default);
                    result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
                }
                var fault = new ReceiptCommitFault();
                await using (var db = Context(fault))
                {
                    Func<Task> complete = () => Complete(db, actorA, 11, multi ? itemId : null);
                    await complete.Should().ThrowAsync<IOException>(); fault.Fired.Should().BeTrue();
                }
                var contextId = multi ? itemId + ":" + itemId : itemId.ToString();
                Guid receiptA;
                await using (var db = Context())
                {
                    var item = await db.WorkItems.SingleAsync(w => w.Id == itemId);
                    item.Status.Should().Be(transfer == "Release" ? WorkItemStatus.Pending : WorkItemStatus.Claimed); item.FormProgressJson.Should().BeNull(); item.FormDataJson.Should().BeNull();
                    var old = await Forms(db, actorA).Gateway.GetByClientIdAsync(form.Id, keyA, "WorkItem", contextId, default);
                    old.Should().NotBeNull(); receiptA = old!.SubmissionId; old.SubmittedBy.Should().Be(actorA.ToString());
                    if (!multi && transfer == "Delegate")
                    {
                        var attempt = item.FormReceiptAttempt; var draftKey = item.FormDraftKey(item.Id);
                        Func<Task> down = () => db.GetService<IMigrator>().MigrateAsync("20261110232446_MappingStatusEligibility");
                        (await down.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51041);
                        await db.Entry(item).ReloadAsync(); item.FormReceiptAttempt.Should().Be(attempt); item.FormDraftKey(item.Id).Should().Be(draftKey);
                        item.CompletedFormReceiptKey.Should().BeNull("this first boundary protects an active attempt with an orphaned native receipt");
                        (await Forms(db, actorA).Gateway.GetByClientIdAsync(form.Id, keyA, "WorkItem", contextId, default))!.SubmissionId.Should().Be(receiptA);
                    }
                    if (transfer == "Release")
                    {
                        (await db.FormReceiptReservations.AnyAsync(r => r.ReceiptKey == keyA)).Should().BeFalse();
                        (await forms.WorkflowReceiptKeys.AnyAsync(r => r.ClientSubmissionId == keyA)).Should().BeTrue();
                        // Neither another context nor another form can squat the committed orphan key.
                        var squat = new FormSubmissionRequest { FormDefinitionId = Guid.NewGuid(), VersionNo = 1, ContextType = "Standalone", ClientSubmissionId = keyA, Answers = new Dictionary<string, object?>() };
                        (await Forms(db, actorA).Service.SubmitAsync(squat, default)).Error.Code.Should().Be("Workflow.Form.Context");
                        item.Claim(actorA, now);
                    }
                    if (transfer == "Delegate") item.Delegate(actorB, now.AddSeconds(1));
                    else { if (transfer == "Release") item.Release(now.AddSeconds(1)); else item.Reassign(group, now.AddSeconds(1)); item.Claim(actorB, now.AddSeconds(2)); }
                    await db.SaveChangesAsync(); keyB = item.FormReceiptKey(itemId, actorB); keyB.Should().NotBe(keyA);
                    var malicious = new FormSubmissionRequest { FormDefinitionId = form.Id, VersionNo = 1, ContextType = "WorkItem", ContextId = contextId, ClientSubmissionId = keyB, Answers = new Dictionary<string, object?> { ["meter_reading"] = 999 } };
                    (await Forms(db, actorA).Service.SubmitAsync(malicious, default)).IsFailure.Should().BeTrue();
                }
                // A second lost workflow response reuses B's receipt and never adopts A's answers.
                fault = new ReceiptCommitFault();
                await using (var db = Context(fault))
                { Func<Task> complete = () => Complete(db, actorB, 22, multi ? itemId : null); await complete.Should().ThrowAsync<IOException>(); }
                await using (var db = Context())
                {
                    await Complete(db, actorB, 999, multi ? itemId : null);
                    await Complete(db, actorB, 999, multi ? itemId : null);
                    var item = await db.WorkItems.SingleAsync(w => w.Id == itemId);
                    if (multi)
                    {
                        var progress = ActivityFormProgress.Read(item.FormProgressJson, config.FormSteps, itemId);
                        progress.Visits.Single().ReceiptKey.Should().Be(keyB); progress.Visits.Single().ActorId.Should().Be(actorB);
                        await Complete(db, actorB, 33, progress.VisitId);
                    }
                    item.Status.Should().Be(WorkItemStatus.Completed);
                    var gateway = Forms(db, actorB).Gateway;
                    var b = (await gateway.GetByClientIdAsync(form.Id, keyB, "WorkItem", contextId, default))!;
                    b.SubmittedBy.Should().Be(actorB.ToString()); Convert.ToDecimal(b.Answers["meter_reading"]).Should().Be(22);
                    var a = (await gateway.GetByClientIdAsync(form.Id, keyA, "WorkItem", contextId, default))!;
                    a.SubmissionId.Should().Be(receiptA); Convert.ToDecimal(a.Answers["meter_reading"]).Should().Be(11);
                    (await db.WorkflowInstances.SingleAsync(i => i.Id == rootId)).Status.Should().Be(WorkflowInstanceStatus.Completed);
                    if (!multi && transfer == "Delegate")
                    {
                        item.CompletedFormReceiptKey.Should().Be(keyB); keyB.Should().NotBe(item.Id);
                        var attempt = item.FormReceiptAttempt; var progress = item.FormProgressJson; var answers = item.FormDataJson;
                        Func<Task> down = () => db.GetService<IMigrator>().MigrateAsync("20261110232446_MappingStatusEligibility");
                        (await down.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(51041);
                        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain("20261111141112_WorkflowFormReceiptOwnership");
                        await db.Database.MigrateAsync(); // Failed Down must leave a subsequent Up a harmless no-op.
                        await db.Entry(item).ReloadAsync(); item.CompletedFormReceiptKey.Should().Be(keyB); item.FormReceiptAttempt.Should().Be(attempt);
                        item.FormDataJson.Should().Be(answers); item.FormProgressJson.Should().Be(progress);
                        (await db.FormReceiptReservations.AnyAsync(r => r.ReceiptKey == keyB)).Should().BeTrue();
                        var preserved = (await gateway.GetByClientIdAsync(form.Id, item.CompletedFormReceiptKey!.Value, "WorkItem", contextId, default))!;
                        preserved.SubmissionId.Should().Be(b.SubmissionId); preserved.SubmittedBy.Should().Be(actorB.ToString());
                        Convert.ToDecimal(preserved.Answers["meter_reading"]).Should().Be(22);
                    }
                }
                var queries = new ReceiptQueries();
                await using (var db = Context(queries))
                {
                    var guard = new WorkflowFormSubmissionGuard(db);
                    (await guard.ValidateAsync(form.Id, 1, "Standalone", null, keyB, new Dictionary<string, object?>(), actorB.ToString(), default)).IsFailure.Should().BeTrue();
                    (await guard.ValidateAsync(form.Id, 1, "Standalone", null, Guid.NewGuid(), new Dictionary<string, object?>(), actorA.ToString(), default)).IsSuccess.Should().BeTrue();
                    queries.Commands.Should().HaveCount(2).And.OnlyContain(q => q.Contains("form_receipt_reservations") && q.Contains("ReceiptKey") && !q.Contains("LIKE") && !q.Contains("FormProgressJson") && !q.Contains("mapping_invocations"));
                }
            }
        var receiptCount = await forms.WorkflowReceiptKeys.CountAsync(); receiptCount.Should().Be(15);
        await forms.GetService<IMigrator>().MigrateAsync("20260927181658_AddFormFieldActivity");
        (await forms.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [FormEngine].[SUB_RECEIPTS]").SingleAsync()).Should().Be(receiptCount);
        await forms.Database.MigrateAsync(); forms.ChangeTracker.Clear();
        (await forms.WorkflowReceiptKeys.CountAsync()).Should().Be(receiptCount, "upgrade backfills immutable history, including orphan receipts from rolled-back claims");
        (await forms.FormDefinitions.SingleAsync()).Id.Should().Be(form.Id);
    }
}
