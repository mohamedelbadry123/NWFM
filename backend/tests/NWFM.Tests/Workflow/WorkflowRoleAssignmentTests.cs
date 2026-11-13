namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

public sealed partial class WorkflowRuntimeEnginePathTests
{
    private (WorkflowRoleAssignments Service, Mock<IWorkflowRoleDirectory> Directory, Guid First, Guid Second) RoleAssignments()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var directory = new Mock<IWorkflowRoleDirectory>();
        directory.Setup(d => d.GetScopeForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.Unrestricted());
        directory.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new(first, "REVIEWER", "Reviewer", "Reviewer"), new(second, "OPERATOR", "Operator", "Operator")]);
        directory.Setup(d => d.GetUserIdsForRolesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return (new(_db, directory.Object, Mock.Of<IWorkflowAssignmentGroupRepository>()), directory, first, second);
    }

    [Fact]
    public async Task Roles_EnforceLiveTerritoryForInboxAndActions()
    {
        var roles = RoleAssignments(); var user = Guid.NewGuid(); var now = DateTime.UtcNow;
        _db.Participants.Add(WorkflowParticipant.Create(_orgId, user, "Reviewer", "reviewer@example.test", now));
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(new { assignmentRoleIds = new[] { roles.First } }), now);
        await BuildEngine(_db, roleAssignments: roles.Service).StartAsync(_orgId, seed.Binding.Id, "territory", "territory-once", now, pinnedWorkflowVersionId: seed.Version);
        var task = await _db.WorkItems.SingleAsync();
        var instance = await _db.WorkflowInstances.SingleAsync(); instance.SetExecutionContext(new OrgLocation("EAST", "E1", null, null), false);
        await _db.SaveChangesAsync();
        roles.Directory.Setup(d => d.GetRoleIdsForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync([roles.First]);
        roles.Directory.Setup(d => d.GetScopeForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.FromRows([new(OrgLevels.Cbu, "WEST1", null)], OrgHierarchy.Empty));
        (await roles.Service.CanAccessAsync(task, user, default)).Should().BeFalse();
        (await roles.Service.AvailableAsync(_orgId, user, false, default)).Should().BeEmpty();
        roles.Directory.Setup(d => d.GetScopeForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.FromRows([new(OrgLevels.Cbu, "E1", null)], OrgHierarchy.Empty));
        (await roles.Service.CanAccessAsync(task, user, default)).Should().BeTrue();
        (await roles.Service.AvailableAsync(_orgId, user, false, default)).Should().ContainSingle(w => w.Id == task.Id);
    }

    [Fact]
    public async Task Roles_RuntimeCreatesOneTaskWithTwoRoleCandidates_WithoutResolvingAGroup()
    {
        var roles = RoleAssignments(); var now = DateTime.UtcNow;
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(new { assignmentRoleIds = new[] { roles.First, roles.Second } }), now);
        var started = await BuildEngine(_db, roleAssignments: roles.Service).StartAsync(_orgId, seed.Binding.Id, "roles", "roles-once", now, pinnedWorkflowVersionId: seed.Version);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : "");
        var task = await _db.WorkItems.SingleAsync(); task.AssignmentGroupId.Should().BeEmpty();
        var candidates = await _db.WorkItemCandidates.Where(c => c.WorkItemId == task.Id).ToListAsync();
        candidates.Should().HaveCount(2).And.OnlyContain(c => c.CandidateType == WorkItemCandidateType.Role);
        candidates.Select(c => c.ReferenceId).Should().BeEquivalentTo(new Guid?[] { roles.First, roles.Second });
        _assignmentResolver.Verify(r => r.ResolveGroupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Roles_UseAnySelectedRole_AndRevocationImmediatelyRemovesEligibilityAndInboxEntry()
    {
        var roles = RoleAssignments(); var user = Guid.NewGuid(); var now = DateTime.UtcNow;
        var participant = WorkflowParticipant.Create(_orgId, user, "Reviewer", "reviewer@example.test", now);
        _db.Participants.Add(participant);
        var task = WorkItem.Create(_orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, now, now.AddMinutes(-1));
        _db.WorkItems.Add(task);
        _db.WorkItemCandidates.AddRange(await roles.Service.CreateCandidatesAsync(_orgId, task.Id, [roles.First, roles.Second], now, default));
        await _db.SaveChangesAsync();
        roles.Directory.Setup(d => d.GetRoleIdsForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync([roles.Second]);
        (await roles.Service.CanAccessAsync(task, user, default)).Should().BeTrue();
        (await roles.Service.AvailableAsync(_orgId, user, false, default)).Should().ContainSingle(w => w.Id == task.Id);
        (await roles.Service.AvailableAsync(_orgId, user, true, default)).Should().ContainSingle(w => w.Id == task.Id);
        roles.Directory.Setup(d => d.GetRoleIdsForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        (await roles.Service.CanAccessAsync(task, user, default)).Should().BeFalse();
        (await roles.Service.AvailableAsync(_orgId, user, false, default)).Should().BeEmpty();
        roles.Directory.Setup(d => d.GetRoleIdsForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync([roles.First]);
        participant.Deactivate(now);
        await _db.SaveChangesAsync();
        (await roles.Service.CanAccessAsync(task, user, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Roles_RejectForeignTenantAndUnregisteredUsers_EvenWhenTheyHoldTheRole()
    {
        var roles = RoleAssignments(); var user = Guid.NewGuid();
        roles.Directory.Setup(d => d.GetRoleIdsForUserAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync([roles.First]);
        var item = WorkItem.Create(_orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, DateTime.UtcNow);
        _db.WorkItems.Add(item); _db.WorkItemCandidates.AddRange(await roles.Service.CreateCandidatesAsync(_orgId, item.Id, [roles.First], DateTime.UtcNow, default));
        await _db.SaveChangesAsync();
        (await roles.Service.CanAccessAsync(item, user, default)).Should().BeFalse();
        _db.Participants.Add(WorkflowParticipant.Create(_orgId, user, "Reviewer", "reviewer@example.test", DateTime.UtcNow)); await _db.SaveChangesAsync();
        var foreign = WorkItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, DateTime.UtcNow);
        (await roles.Service.CanAccessAsync(foreign, user, default)).Should().BeFalse();
        (await roles.Service.AvailableAsync(foreign.OrganizationId, user, false, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Roles_SnapshotDistinctActiveTenantParticipants_WithoutMakingUserSnapshotsAuthorization()
    {
        var roles = RoleAssignments(); var user = Guid.NewGuid(); var inactive = Guid.NewGuid(); var unregistered = Guid.NewGuid();
        _db.Participants.Add(WorkflowParticipant.Create(_orgId, user, "Eligible", "eligible@example.test", DateTime.UtcNow));
        var disabled = WorkflowParticipant.Create(_orgId, inactive, "Inactive", "inactive@example.test", DateTime.UtcNow); disabled.Deactivate(DateTime.UtcNow); _db.Participants.Add(disabled);
        await _db.SaveChangesAsync();
        roles.Directory.Setup(d => d.GetUserIdsForRolesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([user, user, inactive, unregistered]);
        var candidates = await roles.Service.CreateCandidatesAsync(_orgId, Guid.NewGuid(), [roles.First, roles.Second], DateTime.UtcNow, default);
        candidates.Where(c => c.CandidateType == WorkItemCandidateType.User).Should().ContainSingle(c => c.ReferenceId == user);
        candidates.Where(c => c.CandidateType == WorkItemCandidateType.Role).Should().HaveCount(2);
    }

    [Fact]
    public async Task Roles_RejectUnknownDuplicateAndEmptySelections()
    {
        var roles = RoleAssignments();
        foreach (var ids in new List<Guid>[] { [], [roles.First, roles.First], [Guid.Empty], [Guid.NewGuid()] })
        {
            var create = () => roles.Service.CreateCandidatesAsync(_orgId, Guid.NewGuid(), ids, DateTime.UtcNow, default);
            await create.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Roles_PublishingRejectsRemovedRoles_AndAcceptsExistingRoleIdentities()
    {
        var roles = RoleAssignments(); var now = DateTime.UtcNow;
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(new { assignmentRoleIds = new[] { roles.First, roles.Second } }), now);
        var repository = new global::Workflow.Infrastructure.Persistence.Repositories.WorkflowVersionRepository(_db);
        var version = (await repository.GetByIdWithProjectionAsync(seed.Version))!;
        var publisher = new WorkflowWorkspacePublisher(_db, Mock.Of<IWorkflowReferenceData>(), repository,
            Mock.Of<NWFM.Shared.Integration.Organization.IOrgDirectory>(), roleDirectory: roles.Directory.Object);
        (await publisher.ValidateAsync(version, default)).Should().NotContain(e => e.Code.StartsWith("ASSIGNMENT_ROLES"));
        roles.Directory.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        (await publisher.ValidateAsync(version, default)).Should().Contain(e => e.Code == "ASSIGNMENT_ROLES_INVALID");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Roles_BindingReadinessAndActivationRequireExistingRoles(bool available)
    {
        var roles = RoleAssignments(); var now = DateTime.UtcNow;
        var seed = SeedSingleActivity(ActivityType.UserTask, Config(new { assignmentRoleIds = new[] { roles.First } }), now);
        var versions = new global::Workflow.Infrastructure.Persistence.Repositories.WorkflowVersionRepository(_db);
        var version = (await versions.GetByIdWithProjectionAsync(seed.Version))!;
        var binding = WorkflowBinding.Create(version.WorkflowDefinitionId, _orgId, "test", "test", "created", now, mode: WorkflowBindingMode.Active);
        var bindings = new Mock<IWorkflowBindingRepository>();
        bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        var versionRepo = new Mock<IWorkflowVersionRepository>();
        versionRepo.Setup(r => r.GetLatestPublishedWithProjectionAsync(binding.WorkflowDefinitionId, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var gate = new Mock<global::Workflow.Application.Abstractions.IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(NWFM.Shared.Results.Result.Success());
        if (!available) roles.Directory.Setup(d => d.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var readiness = new global::Workflow.Application.Queries.GetWorkflowBindingReadiness.GetWorkflowBindingReadinessQueryHandler(gate.Object, bindings.Object, versionRepo.Object, Mock.Of<IWorkflowAssignmentGroupRepository>(), roles.Directory.Object);
        var ready = await readiness.Handle(new(binding.Id), default);
        ready.IsSuccess.Should().BeTrue(); ready.Value.IsReady.Should().Be(available);
        var activation = new global::Workflow.Application.Commands.ActivateWorkflowBinding.ActivateWorkflowBindingCommandHandler(gate.Object, bindings.Object, versionRepo.Object, Mock.Of<IWorkflowAssignmentGroupRepository>(), Mock.Of<IWorkflowDefinitionRepository>(), roles.Directory.Object);
        (await activation.Handle(new(binding.Id), default)).IsSuccess.Should().Be(available);
    }
}
