using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;
using Tenancy.Api.Middleware;

namespace NWFM.Tests.Tenancy;

public sealed class TenantResolutionTests
{
    private readonly TenantInfo _alpha = TestTenants.Active(key: "alpha");
    private readonly TenantInfo _beta = TestTenants.Active(key: "beta");

    [Fact]
    public async Task AnAnonymousRequestIsBoundByItsHost()
    {
        var (resolved, http) = await ResolveAsync(host: "alpha.localhost");
        Assert.Equal(_alpha.Id, resolved?.Id);
        Assert.Equal(_alpha.Id, http.Items[TenantResolutionMiddleware.TenantIdItem]);
    }

    [Fact]
    public async Task AnUnknownHostWithNothingElseIsRefusedNeverDefaulted()
    {
        var (resolved, http) = await ResolveAsync(host: "nobody.localhost");
        Assert.Null(resolved);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task TheDevelopmentHeaderIsHonouredOnlyWhenEnabled()
    {
        var (enabled, _) = await ResolveAsync(header: "beta", allowHeader: true);
        var (disabled, http) = await ResolveAsync(header: "beta", allowHeader: false);
        Assert.Equal(_beta.Id, enabled?.Id);
        Assert.Null(disabled);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task AHeaderThatContradictsTheHostIsAMismatch()
    {
        var (resolved, http) = await ResolveAsync(host: "alpha.localhost", header: "beta", allowHeader: true);
        Assert.Null(resolved);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task ATokenReplayedOnAnotherTenantsHostIsRefused()
    {
        var (resolved, http) = await ResolveAsync(host: "beta.localhost", token: _alpha);
        Assert.Null(resolved);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task AHeaderNeverSelectsATenantForABearerRequest()
    {
        var (resolved, _) = await ResolveAsync(header: "beta", allowHeader: true, token: _alpha, sendAuthorizationHeader: true);
        Assert.Equal(_alpha.Id, resolved?.Id);
    }

    [Fact]
    public async Task ATenantThatIsNotServingIsRefused()
    {
        var suspended = TestTenants.WithStatus(TenantStatus.Suspended, key: "frozen");
        var (resolved, http) = await ResolveAsync(host: "frozen.localhost", extra: suspended);
        Assert.Null(resolved);
        Assert.Equal(403, http.Response.StatusCode);
    }

    [Fact]
    public async Task AFailedReadinessGateRefusesTheTenant()
    {
        var gate = new Mock<ITenantReadiness>();
        gate.Setup(g => g.CheckAsync(It.IsAny<TenantInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantAccessException.SchemaUpgradePending());
        var (resolved, http) = await ResolveAsync(host: "alpha.localhost", gate: gate.Object);
        Assert.Null(resolved);
        Assert.Equal(503, http.Response.StatusCode);
    }

    [Fact]
    public async Task OperationalPathsAreBoundToNoTenant()
    {
        var context = new TenantContextAccessor();
        var middleware = Middleware(context, allowHeader: false, next: _ =>
        {
            Assert.True(context.IsPlatformScope);
            Assert.Null(context.Current);
            return Task.CompletedTask;
        });
        var http = Http(host: "alpha.localhost", path: "/health");
        using var scope = context.BeginRequestScope();
        await middleware.InvokeAsync(http);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ABinderRefusesATokenWithoutExactlyOneTenantClaim(int claims)
    {
        var binder = new TenantSessionBinder(new TenantContextAccessor(), Store(), []);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            Enumerable.Range(0, claims).Select(_ => new Claim(TenantClaimTypes.TenantId, _alpha.Id.ToString())), "Bearer"));
        Assert.NotNull(await binder.BindAsync(principal, default));
    }

    [Fact]
    public async Task ABinderRefusesATokenForATenantOtherThanTheOneAlreadyBound()
    {
        var context = new TenantContextAccessor();
        using var scope = context.BeginScope(_beta);
        var binder = new TenantSessionBinder(context, Store(), []);
        var refusal = await binder.BindAsync(Principal(_alpha), default);
        Assert.Equal(TenancyErrorCodes.TenantMismatch, refusal?.Code);
    }

    private async Task<(TenantInfo? Resolved, DefaultHttpContext Http)> ResolveAsync(
        string host = "platform.example", string? header = null, bool allowHeader = false, TenantInfo? token = null,
        bool sendAuthorizationHeader = false, TenantInfo? extra = null, ITenantReadiness? gate = null)
    {
        var context = new TenantContextAccessor();
        TenantInfo? resolved = null;
        var store = Store(extra);
        var middleware = new TenantResolutionMiddleware(_ => { resolved = context.Current; return Task.CompletedTask; },
            context, store, Options.Create(new TenancyOptions { RootDomain = "localhost", AllowTenantHeader = allowHeader }),
            NullLogger<TenantResolutionMiddleware>.Instance);
        var http = Http(host, "/api/v1/resources");
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(context)
            .AddSingleton<ITenantSessionBinder>(new TenantSessionBinder(context, store, gate is null ? [] : [gate]));
        if (gate is not null) services.AddSingleton(gate);
        http.RequestServices = services.BuildServiceProvider();
        if (header is not null) http.Request.Headers[TenantHttpHeaders.Tenant] = header;
        if (token is not null)
        {
            http.User = Principal(token);
            if (sendAuthorizationHeader) http.Request.Headers.Authorization = "Bearer token";
        }

        using var scope = context.BeginRequestScope();
        await middleware.InvokeAsync(http);
        return (resolved, http);
    }

    private TenantResolutionMiddleware Middleware(ITenantContext context, bool allowHeader, RequestDelegate next) =>
        new(next, context, Store(), Options.Create(new TenancyOptions { RootDomain = "localhost", AllowTenantHeader = allowHeader }),
            NullLogger<TenantResolutionMiddleware>.Instance);

    private ITenantStore Store(TenantInfo? extra = null)
    {
        TenantInfo[] all = extra is null ? [_alpha, _beta] : [_alpha, _beta, extra];
        var store = new Mock<ITenantStore>();
        store.Setup(s => s.FindById(It.IsAny<Guid>())).Returns<Guid>(id => all.FirstOrDefault(t => t.Id == id));
        store.Setup(s => s.FindByKey(It.IsAny<string>())).Returns<string>(key => all.FirstOrDefault(t => t.Key == key));
        store.Setup(s => s.FindByHost(It.IsAny<string>())).Returns<string>(host =>
            host.EndsWith(".localhost", StringComparison.Ordinal) ? all.FirstOrDefault(t => t.Key == host[..^".localhost".Length]) : null);
        store.Setup(s => s.GetAll()).Returns(all);
        return store.Object;
    }

    private static ClaimsPrincipal Principal(TenantInfo tenant) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(TenantClaimTypes.TenantId, tenant.Id.ToString())
    ], "Bearer"));

    private static DefaultHttpContext Http(string host, string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString(host);
        http.Request.Path = path;
        http.Request.Method = "GET";
        http.Response.Body = new MemoryStream();
        return http;
    }
}
