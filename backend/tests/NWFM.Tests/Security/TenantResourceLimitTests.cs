using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NWFM.Api.Hosting;
using NWFM.Tests.Tenancy;

namespace NWFM.Tests.Security;

public sealed class TenantResourceLimitTests
{
    [Fact]
    public void PublicSignInMetadataDoesNotConsumeTheCredentialAttemptBudget()
    {
        using var limits = new TenantResourceLimits(Options.Create(new TenantResourceOptions
        {
            LoginRequestsPerMinute = 1,
            PrincipalRequestsPerMinute = 2
        }));
        var tenant = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/auth/sso/status";
        using var metadata = limits.AcquirePrincipal(tenant, http);
        using var repeated = limits.AcquirePrincipal(tenant, http);
        using var generalDenied = limits.AcquirePrincipal(tenant, http);
        http.Request.Path = "/api/v1/auth/login";
        using var login = limits.AcquirePrincipal(tenant, http);
        using var authDenied = limits.AcquirePrincipal(tenant, http);
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
        using var limits = new TenantResourceLimits(Options.Create(new TenantResourceOptions
        {
            LoginRequestsPerMinute = 1,
            PrincipalRequestsPerMinute = 3
        }));
        var tenant = Guid.NewGuid();
        var http = Request("alice");
        http.Request.Path = path;
        using var first = limits.AcquirePrincipal(tenant, http);
        http.User = Request("bob").User;
        using var denied = limits.AcquirePrincipal(tenant, http);
        http.Request.Path = "/api/v1/auth/me";
        using var ordinary = limits.AcquirePrincipal(tenant, http);
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(ordinary.IsAcquired);
    }

    [Fact]
    public void ExhaustingOnePrincipalDoesNotConsumeAnotherPrincipalsAllowance()
    {
        using var limits = Create();
        var tenant = Guid.NewGuid();
        var alice = Request("alice");
        using var first = limits.AcquirePrincipal(tenant, alice);
        using var denied = limits.AcquirePrincipal(tenant, alice);
        using var bob = limits.AcquirePrincipal(tenant, Request("bob"));
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(bob.IsAcquired);
    }

    [Fact]
    public void TheSamePrincipalNameInAnotherTenantHasItsOwnAllowance()
    {
        using var limits = Create();
        using var first = limits.AcquirePrincipal(Guid.NewGuid(), Request("alice"));
        using var other = limits.AcquirePrincipal(Guid.NewGuid(), Request("alice"));
        Assert.True(first.IsAcquired);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void OneTenantExhaustingItsBudgetDoesNotThrottleAnother()
    {
        using var limits = Create();
        var a = TestTenants.Active(key: "alpha");
        var b = TestTenants.Active(key: "beta");
        using var first = limits.AcquireRequest(a);
        using var denied = limits.AcquireRequest(a);
        using var other = limits.AcquireRequest(b);
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void PlanOverrideRaisesOnlyThatTenantsBudget()
    {
        using var limits = Create();
        var premium = TestTenants.Active(requestsPerMinute: 2);
        using var first = limits.AcquireRequest(premium);
        using var second = limits.AcquireRequest(premium);
        using var third = limits.AcquireRequest(premium);
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public void ConcurrencyPermitIsPerTenantAndReleasedAfterRequest()
    {
        using var limits = Create();
        var a = TestTenants.Active(key: "alpha");
        var b = TestTenants.Active(key: "beta");
        using (var active = limits.AcquireConcurrency(a))
        {
            using var denied = limits.AcquireConcurrency(a);
            using var other = limits.AcquireConcurrency(b);
            Assert.True(active.IsAcquired);
            Assert.False(denied.IsAcquired);
            Assert.True(other.IsAcquired);
        }
        using var next = limits.AcquireConcurrency(a);
        Assert.True(next.IsAcquired);
    }

    [Fact]
    public void ProcessAndAddressBudgetsApplyBeforeAnyTenantIsKnown()
    {
        using var limits = new TenantResourceLimits(Options.Create(new TenantResourceOptions
        {
            GlobalConcurrentRequests = 1,
            AddressRequestsPerMinute = 1
        }));
        using (var held = limits.AcquireGlobal())
        {
            using var denied = limits.AcquireGlobal();
            Assert.True(held.IsAcquired);
            Assert.False(denied.IsAcquired);
        }
        using var address = limits.AcquireAddress(IPAddress.Loopback);
        using var again = limits.AcquireAddress(IPAddress.Loopback);
        using var otherAddress = limits.AcquireAddress(IPAddress.Parse("10.0.0.7"));
        Assert.True(address.IsAcquired);
        Assert.False(again.IsAcquired);
        Assert.True(otherAddress.IsAcquired);
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
        Assert.Throws<InvalidOperationException>(() => RuntimeStartupGuard.RefuseDatabaseInitialization(options));
    }

    private static TenantResourceLimits Create() => new(Options.Create(new TenantResourceOptions
    {
        RequestsPerMinute = 1,
        PrincipalRequestsPerMinute = 1,
        ConcurrentRequests = 1
    }));

    private static DefaultHttpContext Request(string user)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/workflow/definitions";
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
        return http;
    }
}
