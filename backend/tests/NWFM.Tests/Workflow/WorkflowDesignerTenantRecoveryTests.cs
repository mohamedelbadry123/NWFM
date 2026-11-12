namespace NWFM.Tests.Modules.Workflow;

using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence;
using global::Workflow.Application.Abstractions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using NWFM.Tests.Security;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    [Fact]
    public async Task RevokedStarterScopeStopsRealMappingRecoveryAndFrozenDelivery()
    {
        await using var auth = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = new ApplicationUser { Id = Guid.NewGuid(), UserName = "starter", SecurityStamp = "stamp" };
        var role = new ApplicationRole(Roles.Monitor) { Id = Guid.NewGuid() };
        var scope = OrgScope.Create(OrgScopeOwnerTypes.User, actor.Id.ToString(), null, null, ["OPS"]);
        auth.AddRange(actor, role, scope, new IdentityUserRole<Guid> { UserId = actor.Id, RoleId = role.Id }); await auth.SaveChangesAsync();
        var access = TenantSessionTests.ActorProvider(auth);
        var s = MappingServices(); using var local = new MappingLocalServer();
        var connection = await Connection(s.Api, "Http", local.Address, "None"); var seed = await TypedSeed(connection);
        var runtime = new WorkflowMappingRuntime(_db, new MappingSourceResolver(_db, access, s.Forms.Object), s.Services.Object);
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "revoked", "key", "activity", DateTime.UtcNow, startedByUserId: actor.Id);
        var activity = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", DateTime.UtcNow);
        _db.AddRange(root, activity); await _db.SaveChangesAsync();
        Assert.True((await runtime.BeginGraphAsync(root.Id, activity.Id, seed.Node.ConfigurationJson!, "frozen", default)).IsSuccess);
        var frozen = await _db.MappingInvocations.SingleAsync(); Assert.Equal("Prepared", frozen.Status);
        var pending = WorkflowMappingInvocation.Create(_orgId, root.Id, activity.Id, null, actor.Id, root.Id, seed.Version.Id,
            "activity", "Callable", seed.Node.ConfigurationJson!, "pending", DateTime.UtcNow);
        _db.Add(pending); await _db.SaveChangesAsync();
        auth.Entry(scope).Property(x => x.IsActive).CurrentValue = false; await auth.SaveChangesAsync();
        await runtime.RecoverAsync(default, (_, _) => Task.CompletedTask);
        await _db.Entry(pending).ReloadAsync(); Assert.Equal("Attention", pending.Status); Assert.Contains("denied", pending.Error);
        var job = await _db.IntegrationJobs.SingleAsync();
        await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: runtime).ProcessJobAsync(job.Id, default);
        Assert.Empty(local.Bodies); Assert.Single(await _db.IntegrationJobs.ToListAsync());
        Assert.Null(pending.PreparedRequestJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonAdmissionAuthorizationPoisonDoesNotStarveAndDiagnosticPersistenceReadmits(bool offboardDuringDiagnostic)
    {
        var s = MappingServices(); using var local = new MappingLocalServer(); var seed = await TypedSeed(await Connection(s.Api, "Http", local.Address, "None"));
        var now = DateTime.UtcNow; var actor = Guid.NewGuid();
        var root = WorkflowInstance.Start(_orgId, seed.Binding.Id, seed.Version.Id, "root", "key", "activity", now, startedByUserId: actor);
        var origin = ActivityInstance.Start(_orgId, root.Id, "activity", ActivityType.ServiceTask, "Event", now);
        var foreignTenant = Guid.NewGuid();
        var foreign = WorkflowInstance.Start(foreignTenant, seed.Binding.Id, seed.Version.Id, "foreign", "key", "activity", now);
        await using (var other = new WorkflowDbContext((DbContextOptions<WorkflowDbContext>)_db.GetService<IDbContextOptions>(), new StubTenant(foreignTenant)))
        { other.Add(foreign); await other.SaveChangesAsync(); }
        var bad = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, foreign.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "bad", now.AddSeconds(-1));
        var good = WorkflowMappingInvocation.Create(_orgId, root.Id, origin.Id, null, actor, root.Id, seed.Version.Id, "activity", "Callable", seed.Node.ConfigurationJson!, "good", now);
        _db.AddRange(root, origin, bad, good); await _db.SaveChangesAsync(); var admissions = 0;
        Task Admit(Guid _, CancellationToken ct)
        {
            if (++admissions == 2 && offboardDuringDiagnostic) throw new UnauthorizedAccessException("offboarded before diagnostic");
            return Task.CompletedTask;
        }
        if (offboardDuringDiagnostic)
        {
            await Assert.ThrowsAsync<WorkflowTenantAdmissionRefusedException>(() => s.Runtime.RecoverAsync(default, Admit));
            Assert.Equal(2, admissions);
            Assert.Equal("Preparing", (await _db.MappingInvocations.AsNoTracking().SingleAsync(i => i.Id == bad.Id)).Status);
            Assert.Empty(await _db.IntegrationJobs.ToListAsync());
        }
        else
        {
            await s.Runtime.RecoverAsync(default, Admit); Assert.Equal(3, admissions);
            var persisted = await _db.MappingInvocations.AsNoTracking().SingleAsync(i => i.Id == bad.Id);
            Assert.Equal("Attention", persisted.Status); Assert.Contains("Invalid invocation tenant", persisted.Error);
            var job = await _db.IntegrationJobs.SingleAsync(); Assert.Equal(good.Id, job.InvocationId);
            await new WorkflowIntegrationProcessor(_db, s.Api, new WorkflowIntegrationTransport(), Mock.Of<IWorkflowRuntimeEngine>(), mappings: s.Runtime).ProcessJobAsync(job.Id, default);
            Assert.Single(local.Bodies);
        }
    }

    [Fact]
    public async Task CancelledStepSettlementReadmitsBeforeChangingPreparingWorkItem()
    {
        var s = MappingServices(); var item = WorkItem.Create(_orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        item.WaitForMapping();
        var invocation = WorkflowMappingInvocation.Create(_orgId, item.WorkflowInstanceId, item.ActivityInstanceId, null,
            Guid.NewGuid(), item.WorkflowInstanceId, Guid.NewGuid(), "step", "Step", "{}", "cancelled-step", DateTime.UtcNow);
        _db.Entry(invocation).Property(x => x.FormVisitId).CurrentValue = item.Id;
        invocation.Cancel(); _db.AddRange(item, invocation); await _db.SaveChangesAsync();
        var admissions = 0;
        await Assert.ThrowsAsync<WorkflowTenantAdmissionRefusedException>(() => s.Runtime.RecoverAsync(default, (_, _) =>
        { admissions++; throw new UnauthorizedAccessException("offboarded"); }));
        Assert.Equal(1, admissions); await _db.Entry(item).ReloadAsync(); Assert.Equal(WorkItemStatus.Preparing, item.Status);
        await s.Runtime.RecoverAsync(default, (_, _) => Task.CompletedTask);
        await _db.Entry(item).ReloadAsync(); Assert.Equal(WorkItemStatus.Cancelled, item.Status);
    }

    [Fact]
    public async Task OffboardedMappingBatchDoesNotPrepareOrMarkAttentionAfterSelection()
    {
        var services = MappingServices();
        var invocation = WorkflowMappingInvocation.Create(_orgId, Guid.NewGuid(), Guid.NewGuid(), null,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "call", "Callable", "{}", "offboarded", DateTime.UtcNow);
        _db.MappingInvocations.Add(invocation); await _db.SaveChangesAsync();
        var admissions = 0;
        await Assert.ThrowsAsync<WorkflowTenantAdmissionRefusedException>(() => services.Runtime.RecoverAsync(default, (tenant, _) =>
        {
            Assert.Equal(_orgId, tenant); admissions++;
            throw new UnauthorizedAccessException("Tenant is no longer active.");
        }));
        Assert.Equal(1, admissions);
        var persisted = await _db.MappingInvocations.AsNoTracking().SingleAsync();
        Assert.Equal("Preparing", persisted.Status); Assert.Null(persisted.Error); Assert.Null(persisted.PreparedRequestJson);
        Assert.Empty(await _db.IntegrationJobs.ToListAsync());
    }

    [Fact]
    public async Task OffboardedRoutingDeadlineDoesNotAdvanceAfterSelection()
    {
        var gate = new WorkflowRoutingActivation
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            WorkflowInstanceId = Guid.NewGuid(),
            Status = "Waiting",
            DeadlineAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        _db.RoutingActivations.Add(gate); await _db.SaveChangesAsync();
        var admissions = 0;
        await Assert.ThrowsAsync<WorkflowTenantAdmissionRefusedException>(() => BuildEngine(_db).ProcessRoutingDeadlinesAsync(default, (tenant, _) =>
        {
            Assert.Equal(_orgId, tenant); admissions++;
            throw new UnauthorizedAccessException("Tenant is no longer active.");
        }));
        Assert.Equal(1, admissions); await _db.Entry(gate).ReloadAsync();
        Assert.Equal("Waiting", gate.Status); Assert.Null(gate.ReleasedAt);
        Assert.Empty(await _db.IntegrationJobs.ToListAsync());
    }
}
