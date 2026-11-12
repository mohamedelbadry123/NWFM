using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Options;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;

namespace NWFM.Tests.Security;

public sealed partial class TenantSessionTests
{
    [Fact]
    public async Task SameTenantSessionRebuildsCurrentGrantsInsteadOfTrustingTokenPermissions()
    {
        using var fixture = await Fixture.CreateAsync();
        var identity = (ClaimsIdentity)fixture.Principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Administrator));
        identity.AddClaim(new Claim(PermissionClaimTypes.Permission, "forged.permission"));

        var verified = await fixture.ValidateAsync();

        Assert.NotNull(verified);
        Assert.False(verified.IsInRole(Roles.Administrator));
        Assert.False(verified.HasClaim(PermissionClaimTypes.Permission, "forged.permission"));
        Assert.True(verified.HasClaim(PermissionClaimTypes.Permission, "resource.read"));
        Assert.Equal(fixture.TenantId.ToString("D"), verified.FindFirstValue(AppClaimTypes.TenantId));
    }

    [Theory]
    [InlineData("missing-tenant")]
    [InlineData("foreign-tenant")]
    [InlineData("duplicate-tenant")]
    [InlineData("empty-tenant")]
    [InlineData("invalid-session")]
    [InlineData("missing-stamp")]
    [InlineData("anonymous")]
    public async Task InvalidContextIsRejected(string scenario)
    {
        using var fixture = await Fixture.CreateAsync();
        var identity = (ClaimsIdentity)fixture.Principal.Identity!;
        var tenant = identity.FindFirst(AppClaimTypes.TenantId)!;
        switch (scenario)
        {
            case "missing-tenant": identity.RemoveClaim(tenant); break;
            case "foreign-tenant":
                identity.RemoveClaim(tenant);
                identity.AddClaim(new Claim(AppClaimTypes.TenantId, Guid.NewGuid().ToString()));
                break;
            case "duplicate-tenant": identity.AddClaim(tenant); break;
            case "empty-tenant":
                identity.RemoveClaim(tenant);
                identity.AddClaim(new Claim(AppClaimTypes.TenantId, Guid.Empty.ToString()));
                break;
            case "invalid-session":
                identity.RemoveClaim(identity.FindFirst(AppClaimTypes.SessionId)!);
                identity.AddClaim(new Claim(AppClaimTypes.SessionId, "99999"));
                break;
            case "missing-stamp": identity.RemoveClaim(identity.FindFirst(AppClaimTypes.SecurityStamp)!); break;
            case "anonymous": fixture.Principal = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims)); break;
        }
        Assert.Null(await fixture.ValidateAsync());
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("password-reset")]
    [InlineData("disabled")]
    [InlineData("deleted")]
    [InlineData("membership-removed")]
    [InlineData("suspended")]
    public async Task RevokedAccessIsRejectedImmediately(string scenario)
    {
        using var fixture = await Fixture.CreateAsync();
        switch (scenario)
        {
            case "logout": fixture.Session.Revoke(DateTime.UtcNow); break;
            case "password-reset": fixture.User.SecurityStamp = Guid.NewGuid().ToString(); break;
            case "disabled":
                fixture.User.LockoutEnabled = true;
                fixture.User.LockoutEnd = DateTimeOffset.MaxValue;
                break;
            case "deleted": fixture.Db.Users.Remove(fixture.User); break;
            case "membership-removed": fixture.Db.OrgScopes.RemoveRange(fixture.Db.OrgScopes); break;
            case "suspended": fixture.Options.IsActive = false; break;
        }
        await fixture.Db.SaveChangesAsync();
        Assert.Null(await fixture.ValidateAsync());
    }

    [Fact]
    public async Task RemovedRolePermissionIsNotRetainedByAnExistingSession()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.ValidateAsync())!.HasClaim(PermissionClaimTypes.Permission, "resource.read"));
        fixture.Db.RolePermissions.RemoveRange(fixture.Db.RolePermissions);
        await fixture.Db.SaveChangesAsync();
        var verified = await fixture.ValidateAsync();
        Assert.NotNull(verified);
        Assert.False(verified.HasClaim(PermissionClaimTypes.Permission, "resource.read"));
    }

    [Fact]
    public async Task DisabledTeamIsRejectedEvenWithAnActiveSessionAndScope()
    {
        using var fixture = await Fixture.CreateAsync();
        var team = Team.Create("Tenant crew", null);
        fixture.Db.Teams.Add(team);
        fixture.User.TeamId = team.Id;
        var role = await fixture.Db.Roles.SingleAsync();
        role.Name = Roles.FieldTeam;
        fixture.Db.OrgScopes.Add(OrgScope.Create(OrgScopeOwnerTypes.Team, team.Id.ToString("D"),
            null, null, ["OPS"]));
        await fixture.Db.SaveChangesAsync();
        Assert.NotNull(await fixture.ValidateAsync());

        team.Deactivate();
        await fixture.Db.SaveChangesAsync();
        Assert.Null(await fixture.ValidateAsync());
    }

    [Fact]
    public async Task NewTokenContainsServerTenantPersistedSessionAndCurrentSecurityStamp()
    {
        using var fixture = await Fixture.CreateAsync();
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(x => x.FindByNameAsync("tester")).ReturnsAsync(fixture.User);
        users.Setup(x => x.CheckPasswordAsync(fixture.User, "password")).ReturnsAsync(true);
        users.Setup(x => x.IsLockedOutAsync(fixture.User)).ReturnsAsync(false);
        users.Setup(x => x.IsInRoleAsync(fixture.User, Roles.Administrator)).ReturnsAsync(true);
        users.Setup(x => x.GetRolesAsync(fixture.User)).ReturnsAsync(new List<string> { Roles.Administrator });
        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(x => x.GetPermissionCodesForUserAsync(fixture.User.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        var settings = new JwtSettings { SigningKey = new string('k', 64) };
        var service = new AuthService(users.Object, fixture.Db, permissions.Object,
            Mock.Of<IActiveDirectoryAuthenticator>(), new Tenant(fixture.TenantId),
            Microsoft.Extensions.Options.Options.Create(settings),
            Microsoft.Extensions.Options.Options.Create(new SsoSettings()), fixture.Monitor, TimeProvider.System);

        var result = await service.LoginAsync("tester", "password", default);

        Assert.True(result.IsSuccess);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value!.AccessToken);
        Assert.Equal(fixture.TenantId.ToString("D"), token.Claims.Single(x => x.Type == AppClaimTypes.TenantId).Value);
        Assert.Equal(fixture.User.SecurityStamp, token.Claims.Single(x => x.Type == AppClaimTypes.SecurityStamp).Value);
        var sessionId = long.Parse(token.Claims.Single(x => x.Type == AppClaimTypes.SessionId).Value);
        var session = await fixture.Db.UserRefreshTokens.SingleAsync(x => x.Id == sessionId);
        Assert.Equal(fixture.User.Id.ToString(), session.UserId);
        Assert.DoesNotContain(result.Value.RefreshToken, session.TokenHash, StringComparison.Ordinal);
        Assert.StartsWith($"{fixture.User.Id:N}.", result.Value.RefreshToken, StringComparison.Ordinal);
    }

    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;

    private sealed class Fixture : IDisposable
    {
        public AuthDbContext Db { get; }
        public Guid TenantId { get; } = Guid.NewGuid();
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), UserName = "tester", SecurityStamp = Guid.NewGuid().ToString() };
        public UserRefreshToken Session { get; private set; } = null!;
        public ClaimsPrincipal Principal { get; set; } = null!;
        public TenantIsolationOptions Options { get; }
        public IOptionsMonitor<TenantIsolationOptions> Monitor { get; }

        private Fixture()
        {
            Db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Options = new TenantIsolationOptions { TenantId = TenantId };
            var monitor = new Mock<IOptionsMonitor<TenantIsolationOptions>>();
            monitor.SetupGet(x => x.CurrentValue).Returns(Options);
            Monitor = monitor.Object;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var role = new ApplicationRole("Reader") { Id = Guid.NewGuid() };
            var permission = Permission.Create("resource.read", "Tests", "Read", "Read");
            fixture.Db.AddRange(fixture.User, role, permission,
                new IdentityUserRole<Guid> { UserId = fixture.User.Id, RoleId = role.Id },
                RolePermission.Create(role.Id.ToString(), permission.Id),
                OrgScope.Create(OrgScopeOwnerTypes.User, fixture.User.Id.ToString(), null, null, ["OPS"]));
            fixture.Session = UserRefreshToken.Create(fixture.User.Id.ToString(), "opaque-hash",
                DateTime.UtcNow.AddDays(1), DateTime.UtcNow);
            fixture.Db.Add(fixture.Session);
            await fixture.Db.SaveChangesAsync();
            fixture.Principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, fixture.User.Id.ToString()),
                new Claim(AppClaimTypes.TenantId, fixture.TenantId.ToString()),
                new Claim(AppClaimTypes.SessionId, fixture.Session.Id.ToString()),
                new Claim(AppClaimTypes.SecurityStamp, fixture.User.SecurityStamp!)
            ], "Bearer"));
            return fixture;
        }

        public Task<ClaimsPrincipal?> ValidateAsync() =>
            new TenantSessionValidator(Db, new Tenant(TenantId), Monitor, TimeProvider.System)
                .ValidateAsync(Principal, default);

        public void Dispose() => Db.Dispose();
    }
}
