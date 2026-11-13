using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NWFM.Api.Hosting;
using NWFM.Api.Services;

namespace NWFM.Tests.Security;

public sealed class TenantResourceLimitTests
{
    [Fact]
    public void PublicSignInMetadataDoesNotConsumeTheCredentialAttemptBudget()
    {
        using var limits = new TenantResourceLimits(
            Options.Create(new TenantResourceOptions
            {
                LoginRequestsPerMinute = 1,
                PrincipalRequestsPerMinute = 2
            }),
            Options.Create(new ApplicationOptions { TenantId = Guid.NewGuid() }));
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/auth/sso/status";
        using var metadata = limits.AcquirePrincipal(http);
        using var repeated = limits.AcquirePrincipal(http);
        using var generalDenied = limits.AcquirePrincipal(http);
        http.Request.Path = "/api/v1/auth/login";
        using var login = limits.AcquirePrincipal(http);
        using var authDenied = limits.AcquirePrincipal(http);
        Assert.True(metadata.IsAcquired);
        Assert.True(repeated.IsAcquired);
        Assert.False(generalDenied.IsAcquired);
        Assert.True(login.IsAcquired);
        Assert.False(authDenied.IsAcquired);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/team/verify-otp")]
    public void AuthenticationEndpointsUseTheStricterIpBudget(string path)
    {
        using var limits = new TenantResourceLimits(
            Options.Create(new TenantResourceOptions
            {
                LoginRequestsPerMinute = 1,
                PrincipalRequestsPerMinute = 3
            }),
            Options.Create(new ApplicationOptions { TenantId = Guid.NewGuid() }));
        var http = Request("alice");
        http.Request.Path = path;
        using var first = limits.AcquirePrincipal(http);
        http.User = Request("bob").User;
        using var denied = limits.AcquirePrincipal(http);
        http.Request.Path = "/api/v1/auth/me";
        using var ordinary = limits.AcquirePrincipal(http);
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(ordinary.IsAcquired);
    }

    [Fact]
    public void ExhaustingOnePrincipalDoesNotConsumeAnotherPrincipalsAllowance()
    {
        using var limits = Create();
        var alice = Request("alice");
        using var first = limits.AcquirePrincipal(alice);
        using var denied = limits.AcquirePrincipal(alice);
        using var bob = limits.AcquirePrincipal(Request("bob"));
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(bob.IsAcquired);
    }

    [Fact]
    public void DedicatedTenantsHaveIndependentBudgets()
    {
        using var a = Create();
        using var b = Create();
        using var first = a.AcquireRequest();
        using var denied = a.AcquireRequest();
        using var other = b.AcquireRequest();
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void ConcurrencyPermitIsReleasedAfterRequest()
    {
        using var limits = Create();
        using (var active = limits.AcquireConcurrency())
        {
            using var denied = limits.AcquireConcurrency();
            Assert.True(active.IsAcquired);
            Assert.False(denied.IsAcquired);
        }
        using var next = limits.AcquireConcurrency();
        Assert.True(next.IsAcquired);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RequestHostCannotRunPrivilegedInitialization(bool migrate, bool seed, bool sql)
    {
        var options = new NWFM.Shared.Options.DatabaseStartupOptions
        {
            ApplyMigrations = migrate,
            SeedData = seed,
            ApplySqlObjects = sql
        };
        Assert.Throws<InvalidOperationException>(() => TenantDatabaseBoundary.ValidateStartup(options));
    }

    private static TenantResourceLimits Create() => new(
        Options.Create(new TenantResourceOptions
        {
            RequestsPerMinute = 1,
            PrincipalRequestsPerMinute = 1,
            ConcurrentRequests = 1
        }),
        Options.Create(new ApplicationOptions { TenantId = Guid.NewGuid() }));

    private static DefaultHttpContext Request(string user)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/workflow/definitions";
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
        return http;
    }
}
