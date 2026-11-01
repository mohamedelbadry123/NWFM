using System.Security.Claims;
using Auth.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;
using NWFM.Tests.Tenancy;
using Workflow.Infrastructure.Persistence;
using AppContext = NWFM.Api.Services.ApplicationContext;

namespace NWFM.Tests.Security;

public sealed class TenantRequestContextTests
{
    [Theory]
    [InlineData("x-organization-id")]
    [InlineData("x-tenant-id")]
    public async Task ForeignHeaderSelectorCannotReplaceResolvedTenant(string header)
    {
        var tenant = Guid.NewGuid();
        var http = Http();
        http.Request.Headers[header] = Guid.NewGuid().ToString();
        var called = false;
        var middleware = new RequestContextMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RequestContextMiddleware>.Instance);
        await middleware.InvokeAsync(http, Tenants(tenant), new AppContext(), Services(tenant));
        Assert.False(called);
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task HeaderSelectorThatRepeatsTheResolvedTenantIsAllowed()
    {
        var tenant = Guid.NewGuid();
        var http = Http();
        http.Request.Headers["x-tenant-id"] = tenant.ToString();
        var called = false;
        var middleware = new RequestContextMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RequestContextMiddleware>.Instance);
        await middleware.InvokeAsync(http, Tenants(tenant), new AppContext(), Services(tenant));
        Assert.True(called);
    }

    [Theory]
    [InlineData("organizationId=invalid")]
    [InlineData("tenantId=invalid")]
    [InlineData("tenantId=00000000-0000-0000-0000-000000000000")]
    public async Task InvalidQuerySelectorIsDeniedBeforeDataAccess(string query)
    {
        var tenant = Guid.NewGuid();
        var http = Http();
        http.Request.QueryString = new QueryString("?" + query);
        var middleware = new RequestContextMiddleware(_ => throw new InvalidOperationException("must not run"),
            NullLogger<RequestContextMiddleware>.Instance);
        await middleware.InvokeAsync(http, Tenants(tenant), new AppContext(), Services(tenant));
        Assert.Equal(400, http.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutTenantBindingCannotSelectAnActor()
    {
        var tenant = Guid.NewGuid();
        var http = Http();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        ], "Bearer"));
        var context = new AppContext();
        var middleware = new RequestContextMiddleware(_ => throw new InvalidOperationException("must not run"),
            NullLogger<RequestContextMiddleware>.Instance);
        await middleware.InvokeAsync(http, Tenants(tenant), context, Services(tenant));
        Assert.Equal(401, http.Response.StatusCode);
        Assert.Equal(Guid.Empty, context.ActorId);
    }

    [Fact]
    public async Task TokenOfAnotherTenantIsRefusedOnThisTenantsRequest()
    {
        var tenant = Guid.NewGuid();
        var http = Http();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(AppClaimTypes.TenantId, Guid.NewGuid().ToString())
        ], "Bearer"));
        var middleware = new RequestContextMiddleware(_ => throw new InvalidOperationException("must not run"),
            NullLogger<RequestContextMiddleware>.Instance);
        await middleware.InvokeAsync(http, Tenants(tenant), new AppContext(), Services(tenant));
        Assert.Equal(401, http.Response.StatusCode);
    }

    [Fact]
    public async Task VerifiedActorIsAvailableDownstreamAndClearedAfterFailure()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var http = Http();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.ToString()),
            new Claim(AppClaimTypes.TenantId, tenant.ToString())
        ], "Bearer"));
        var context = new AppContext();
        var middleware = new RequestContextMiddleware(_ =>
        {
            Assert.Equal(user, context.ActorId);
            throw new InvalidOperationException("downstream failure");
        }, NullLogger<RequestContextMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(http, Tenants(tenant), context, Services(tenant)));
        Assert.Equal(Guid.Empty, context.ActorId);
        Assert.Equal(Guid.Empty, context.ParticipantId);
    }

    [Theory]
    [InlineData("organizationId")]
    [InlineData("tenantId")]
    [InlineData("body")]
    public void BoundTenantSelectorsAreRejected(string argument)
    {
        var tenant = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var action = new ActionExecutingContext(
            new ActionContext(Http(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?> { [argument] = argument == "body" ? new { TenantId = foreign } : foreign },
            new object());
        new TenantScopeFilter(new Tenant(tenant), NullLogger<TenantScopeFilter>.Instance).OnActionExecuting(action);
        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public void MatchingBodyTenantSelectorIsAllowed()
    {
        var tenant = Guid.NewGuid();
        var action = new ActionExecutingContext(
            new ActionContext(Http(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?> { ["body"] = new { OrganizationId = tenant } }, new object());
        new TenantScopeFilter(new Tenant(tenant), NullLogger<TenantScopeFilter>.Instance).OnActionExecuting(action);
        Assert.Null(action.Result);
    }

    private static DefaultHttpContext Http()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/resources";
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static ITenantContext Tenants(Guid tenant) => TestTenants.Fixed(TestTenants.Active(tenant));

    private static IServiceProvider Services(Guid tenant) => new ServiceCollection()
        .AddSingleton(new WorkflowDbContext(
            new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Tenant(tenant)))
        .BuildServiceProvider();

    private sealed record Tenant(Guid OrganizationId) : ICurrentTenant;
}
