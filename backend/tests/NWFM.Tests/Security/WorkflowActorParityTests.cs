using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Organization;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Options;
using NWFM.Shared.Organization;

namespace NWFM.Tests.Security;

public sealed partial class TenantSessionTests
{
    [Theory]
    [InlineData("Reader", false, false, true, true, true)]
    [InlineData("Reader", false, false, true, false, false)]
    [InlineData(Roles.Monitor, false, false, true, false, false)]
    [InlineData(Roles.Monitor, false, false, true, true, true)]
    [InlineData(Roles.Administrator, false, false, true, false, true)]
    [InlineData(Roles.Administrator, true, false, true, false, false)]
    [InlineData(Roles.Administrator, true, true, false, false, false)]
    [InlineData(Roles.Administrator, true, true, true, false, true)]
    [InlineData(Roles.Monitor, true, false, true, true, false)]
    [InlineData(Roles.Monitor, true, true, false, true, false)]
    [InlineData(Roles.Monitor, true, true, true, false, false)]
    [InlineData(Roles.Monitor, true, true, true, true, true)]
    [InlineData("Reader", true, true, true, true, true)]
    public async Task DurableActorRightsMatchSessionEligibility(string roleName, bool teamUser, bool fieldTeam,
        bool activeTeam, bool scope, bool allowed)
    {
        using var f = await Fixture.CreateAsync();
        f.Db.RemoveRange(await f.Db.UserRoles.ToListAsync());
        f.Db.RemoveRange(await f.Db.OrgScopes.ToListAsync());
        var role = new ApplicationRole(roleName) { Id = Guid.NewGuid() };
        f.Db.AddRange(role, new IdentityUserRole<Guid> { UserId = f.User.Id, RoleId = role.Id });
        if (teamUser)
        {
            var team = Team.Create("Current team", null); if (!activeTeam) team.Deactivate();
            f.User.TeamId = team.Id; f.Db.Add(team);
        }
        if (fieldTeam)
        {
            var field = new ApplicationRole(Roles.FieldTeam) { Id = Guid.NewGuid() };
            f.Db.AddRange(field, new IdentityUserRole<Guid> { UserId = f.User.Id, RoleId = field.Id });
        }
        if (scope) f.Db.Add(OrgScope.Create(teamUser ? OrgScopeOwnerTypes.Team : OrgScopeOwnerTypes.User,
            (f.User.TeamId ?? f.User.Id).ToString(), null, null, ["OPS"]));
        await f.Db.SaveChangesAsync();
        Assert.Equal(allowed, await f.ValidateAsync() is not null);
        var actor = await ActorProvider(f.Db).GetAsync(f.User.Id, default);
        Assert.Equal(allowed, actor is not null);
        if (allowed) Assert.Equal(roleName == Roles.Administrator, actor!.IsAdministrator);
    }

    [Fact]
    public async Task LockedOutAdministratorCannotExecuteThroughEitherBoundary()
    {
        using var f = await Fixture.CreateAsync();
        var role = new ApplicationRole(Roles.Administrator) { Id = Guid.NewGuid() };
        f.Db.AddRange(role, new IdentityUserRole<Guid> { UserId = f.User.Id, RoleId = role.Id });
        f.User.LockoutEnabled = true; f.User.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
        await f.Db.SaveChangesAsync();
        Assert.Null(await f.ValidateAsync()); Assert.Null(await ActorProvider(f.Db).GetAsync(f.User.Id, default));
    }

    [Fact]
    public async Task DurableIdentityDoesNotRequireAnHttpRefreshSessionButStillRequiresCurrentRights()
    {
        using var f = await Fixture.CreateAsync();
        f.Db.Remove(f.Session); await f.Db.SaveChangesAsync();
        Assert.Null(await f.ValidateAsync());
        Assert.NotNull(await ActorProvider(f.Db).GetAsync(f.User.Id, default));
        f.Db.RemoveRange(await f.Db.OrgScopes.ToListAsync()); await f.Db.SaveChangesAsync();
        Assert.Null(await ActorProvider(f.Db).GetAsync(f.User.Id, default));
    }

    internal static IWorkflowActorAccess ActorProvider(AuthDbContext db)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<Func<CancellationToken, ValueTask<OrgHierarchy>>>(),
            It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, ValueTask<OrgHierarchy>> factory, CacheEntryOptions? _, CancellationToken ct) => factory(ct));
        var scopes = new OrgScopeProvider(db, Mock.Of<ICurrentUser>(), cache.Object, Options.Create(new CacheSettings()));
        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(p => p.GetPermissionCodesForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { NwfmPolicies.ManageDefinitions, NwfmPolicies.ViewInstances, NwfmPolicies.ViewSubmissions });
        return new WorkflowActorAccessProvider(db, permissions.Object, scopes, TimeProvider.System);
    }
}
