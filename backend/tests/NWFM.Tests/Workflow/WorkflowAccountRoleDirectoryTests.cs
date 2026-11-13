namespace NWFM.Tests.Modules.Workflow;

using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;

public sealed class WorkflowAccountRoleDirectoryTests
{
    [Fact]
    public async Task Scope_ReadsTheAssignedUsersCoverage_AndAdminOverride_WithoutUsingCallersScope()
    {
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase("roles-" + Guid.NewGuid()).Options);
        var role = new ApplicationRole("Reviewer") { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "reviewer" };
        db.Roles.Add(role); db.Users.Add(user); db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id });
        db.OrgScopes.Add(OrgScope.Create(OrgScopeOwnerTypes.User, OrgScopeOwnerTypes.UserOwnerId(user.Id), OrgLevels.Cbu, "E1", ["WATER"], ["SURVEY"]));
        await db.SaveChangesAsync();
        var scopes = new Mock<IOrgScopeProvider>();
        scopes.Setup(s => s.GetHierarchyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OrgHierarchy.Empty);
        var directory = new WorkflowRoleDirectory(db, scopes.Object);
        var scope = (await directory.GetScopeForUserAsync(user.Id, default))!;
        new OrgLocation(null, "E1", null, null).IsCoveredBy(scope, "WATER", "SURVEY").Should().BeTrue();
        new OrgLocation(null, "OTHER", null, null).IsCoveredBy(scope, "WATER", "SURVEY").Should().BeFalse();
        new OrgLocation(null, "E1", null, null).IsCoveredBy(scope, "WASTE", "SURVEY").Should().BeFalse();
        var admin = new ApplicationRole(Roles.Administrator) { Id = Guid.NewGuid() };
        db.Roles.Add(admin); db.UserRoles.Add(new() { UserId = user.Id, RoleId = admin.Id }); await db.SaveChangesAsync();
        (await directory.GetScopeForUserAsync(user.Id, default))!.IsUnrestricted.Should().BeTrue();
        (await directory.GetScopeForUserAsync(Guid.NewGuid(), default)).Should().BeNull();
        scopes.Verify(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingAccountRoles_AreReadWithoutPermissionGrants_AndRevocationIsLive()
    {
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase("roles-" + Guid.NewGuid()).Options);
        var first = new ApplicationRole("Reviewer") { Id = Guid.NewGuid() };
        var second = new ApplicationRole("Operator") { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "reviewer", LockoutEnabled = true };
        db.Roles.AddRange(first, second); db.Users.Add(user);
        var membership = new IdentityUserRole<Guid> { UserId = user.Id, RoleId = first.Id };
        db.UserRoles.Add(membership); await db.SaveChangesAsync();
        var directory = new WorkflowRoleDirectory(db);
        (await directory.ListAsync(default)).Select(r => r.Id).Should().BeEquivalentTo(new[] { first.Id, second.Id });
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().ContainSingle(id => id == first.Id);
        (await directory.GetUserIdsForRolesAsync([first.Id, second.Id], default)).Should().ContainSingle(id => id == user.Id);
        db.UserRoles.Remove(membership); await db.SaveChangesAsync();
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().BeEmpty();
        (await directory.GetUserIdsForRolesAsync([first.Id], default)).Should().BeEmpty();
        (await db.RolePermissions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LockedAndDeletedAccounts_AndDeletedRolesCannotAuthorizeTaskAccess()
    {
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase("roles-" + Guid.NewGuid()).Options);
        var role = new ApplicationRole("Reviewer") { Id = Guid.NewGuid() };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "reviewer", LockoutEnabled = true, LockoutEnd = DateTimeOffset.UtcNow.AddHours(1) };
        db.Roles.Add(role); db.Users.Add(user); db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
        var directory = new WorkflowRoleDirectory(db);
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().BeEmpty();
        (await directory.GetUserIdsForRolesAsync([role.Id], default)).Should().BeEmpty();
        user.LockoutEnd = null; await db.SaveChangesAsync();
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().ContainSingle();
        db.Roles.Remove(role); await db.SaveChangesAsync();
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().BeEmpty();
        db.Roles.Add(role); db.Users.Remove(user); await db.SaveChangesAsync();
        (await directory.GetRoleIdsForUserAsync(user.Id, default)).Should().BeEmpty();
    }
}
