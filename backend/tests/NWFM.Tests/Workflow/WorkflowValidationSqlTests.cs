namespace NWFM.Tests.Modules.Workflow;

using System.Data.Common;
using FluentAssertions;
using global::Workflow.Api.Controllers;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.PublishWorkflowVersion;
using global::Workflow.Application.Commands.SaveWorkflowDraftXml;
using global::Workflow.Application.Commands.ValidateWorkflowVersion;
using global::Workflow.Application.DTOs;
using global::Workflow.Application.Routing;
using global::Workflow.Application.Workspace;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NWFM.Shared.Results;
using NWFM.Tests.Infrastructure;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private const string ValidationRaceValidXml = """
        <Workflow xmlns="https://privora.io/workflow/v1"><Activities>
        <Activity nodeKey="start" type="Start" name="Start"/><Activity nodeKey="end" type="End" name="End"/>
        </Activities><Transitions><Transition key="finish" from="start" to="end"/></Transitions></Workflow>
        """;
    private static string ValidationRaceInvalidXml => ValidationRaceValidXml.Replace("type=\"End\"", "type=\"Start\"", StringComparison.Ordinal);

    private sealed class DraftLockAttempt : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)) Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [RoutingSqlFact]
    public async Task DraftValidationSqlSerializesAutosaveAndReturnsConflictWhenPublicationWins()
    {
        await using var sql = await OwnedLocalDb.CreateAsync();
        WorkflowDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(sql.ConnectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")).AddInterceptors(interceptors).Options, new StubTenant(_orgId));
        var gate = new Mock<IWorkflowFeatureGate>(); gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success()); var compiler = new WorkflowXmlCompiler();
        SaveWorkflowDraftXmlCommandHandler Saver(WorkflowDbContext db) => new(gate.Object, new WorkflowVersionRepository(db), compiler, draftLock: new WorkflowDraftLock(db));
        ValidateWorkflowVersionCommandHandler Validator(WorkflowDbContext db, IWorkflowWorkspacePublisher? pause = null) => new(gate.Object, new WorkflowVersionRepository(db), compiler, workspace: pause, draftLock: new WorkflowDraftLock(db));
        PublishWorkflowVersionCommandHandler Publisher(WorkflowDbContext db) => new(gate.Object, new WorkflowDefinitionRepository(db), new WorkflowVersionRepository(db), compiler);
        async Task<Guid> Draft()
        {
            await using var db = Context(); var now = DateTime.UtcNow;
            var definition = WorkflowDefinition.Create(_orgId, Guid.NewGuid().ToString(), "Validation race", now);
            var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), now); db.AddRange(definition, version); await db.SaveChangesAsync();
            (await Saver(db).Handle(new(version.Id, ValidationRaceValidXml), default)).IsSuccess.Should().BeTrue(); return version.Id;
        }
        (IWorkflowWorkspacePublisher Publisher, TaskCompletionSource Read, TaskCompletionSource Resume) Pause()
        {
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pause = new Mock<IWorkflowWorkspacePublisher>();
            pause.Setup(p => p.ValidateAsync(It.IsAny<WorkflowVersion>(), It.IsAny<CancellationToken>())).Returns(async (WorkflowVersion _, CancellationToken ct) =>
            { read.TrySetResult(); await resume.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); return Array.Empty<WorkflowValidationIssueDto>(); });
            return (pause.Object, read, resume);
        }
        await using (var db = Context()) await db.Database.MigrateAsync();
        var first = await Draft(); var barrier = Pause(); var writeAttempt = new DraftLockAttempt();
        await using (var validation = Context())
        await using (var autosave = Context(writeAttempt))
        {
            var validate = Validator(validation, barrier.Publisher).Handle(new(first), default);
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var save = Saver(autosave).Handle(new(first, ValidationRaceInvalidXml), default);
            try { await writeAttempt.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); save.IsCompleted.Should().BeFalse("autosave must wait for validation's version lock"); }
            finally { barrier.Resume.TrySetResult(); }
            (await validate).Value.IsValid.Should().BeTrue(); (await save).IsSuccess.Should().BeTrue();
        }
        await using (var verify = Context())
        {
            var version = await verify.WorkflowVersions.SingleAsync(v => v.Id == first);
            version.ValidationStatus.Should().Be(WorkflowValidationStatus.NotValidated, "the later autosave invalidates the earlier validation result");
            (await Validator(verify).Handle(new(first), default)).Value.IsValid.Should().BeFalse();
            (await Publisher(verify).Handle(new(first, Guid.NewGuid()), default)).IsFailure.Should().BeTrue();
            version.Status.Should().Be(WorkflowVersionStatus.Draft);
        }
        // The inverse ordering must reload a cached version only after the writer commits.
        var second = await Draft(); var readAttempt = new DraftLockAttempt();
        await using (var validation = Context(readAttempt))
        await using (var autosave = Context())
        {
            var cached = await validation.WorkflowVersions.SingleAsync(v => v.Id == second); var before = RoutingDraftRevision.Of(cached);
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = new WorkflowDraftLock(autosave).RunAsync(second, async () =>
            { held.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return await Saver(autosave).Handle(new(second, ValidationRaceInvalidXml), default); }, default);
            await held.Task.WaitAsync(TimeSpan.FromSeconds(10)); var validate = Validator(validation).Handle(new(second), default);
            try { await readAttempt.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); validate.IsCompleted.Should().BeFalse(); }
            finally { release.TrySetResult(); }
            (await save).IsSuccess.Should().BeTrue(); (await validate).Value.IsValid.Should().BeFalse();
            RoutingDraftRevision.Of(cached).Should().NotBe(before); cached.ValidationStatus.Should().Be(WorkflowValidationStatus.Invalid);
        }
        // Publication currently uses optimistic rowversion rather than the draft lock. Exercise that
        // real writer, not an injected concurrency exception, and verify the endpoint's 409 body.
        var third = await Draft(); barrier = Pause();
        await using (var validation = Context())
        await using (var publication = Context())
        {
            var validate = Validator(validation, barrier.Publisher).Handle(new(third), default);
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { (await Publisher(publication).Handle(new(third, Guid.NewGuid()), default)).IsSuccess.Should().BeTrue(); }
            finally { barrier.Resume.TrySetResult(); }
            var conflict = await validate; conflict.IsFailure.Should().BeTrue(); conflict.Error.Code.Should().Be("Workflow.Draft.Conflict");
            validation.ChangeTracker.HasChanges().Should().BeFalse(); var published = await publication.WorkflowVersions.SingleAsync(v => v.Id == third); var rowVersion = published.RowVersion.ToArray();
            (await Validator(validation).Handle(new(third), default)).Error.Code.Should().Be("Workflow.Version.NotDraft");
            await publication.Entry(published).ReloadAsync(); published.Status.Should().Be(WorkflowVersionStatus.Published); published.RowVersion.Should().Equal(rowVersion);
            var sender = new Mock<MediatR.ISender>(); sender.Setup(s => s.Send(It.IsAny<ValidateWorkflowVersionCommand>(), default)).ReturnsAsync(conflict);
            var response = (ConflictObjectResult)await new WorkflowVersionsController(sender.Object).Validate(published.WorkflowDefinitionId, third, default);
            response.StatusCode.Should().Be(409); System.Text.Json.JsonSerializer.Serialize(response.Value).Should().Contain("Workflow.Draft.Conflict");
        }
    }
}
