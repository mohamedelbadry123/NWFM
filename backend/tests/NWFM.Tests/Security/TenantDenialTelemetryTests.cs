using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Api.Hosting;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.MultiTenancy;
using NWFM.Tests.Tenancy;

namespace NWFM.Tests.Security;

public sealed class TenantDenialTelemetryTests
{
    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task OuterExceptionHandlerDenialsUseFinalStatusAndRestoreNoStore(int status)
    {
        using var fixture = new Fixture();
        var response = new DeferredResponseFeature();
        var http = Request(response);

        // The production exception handler wraps the budget middlewares. It clears the response
        // and chooses the status after the budget InvokeAsync unwinds.
        var exception = await Record.ExceptionAsync(() => fixture.Invoke(_ => status == 403
            ? throw new UnauthorizedAccessException("private failure")
            : throw new WorkflowQueueCapacityException("private queue"), http));
        Assert.NotNull(exception);
        http.Response.Clear();
        http.Response.StatusCode = exception is UnauthorizedAccessException ? 403 : 429;
        Assert.Empty(fixture.Logger.Events);
        await response.StartAsync();

        fixture.AssertSingleDenial(status);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.DoesNotContain("private", fixture.Logger.Events.Single().Message);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(400, true)]
    [InlineData(403, true)]
    public async Task FinalDeniedResponseRecordsExactlyOneEvent(int status, bool selectorDenied)
    {
        using var fixture = new Fixture();
        var response = new DeferredResponseFeature();
        var http = Request(response);

        await fixture.Invoke(context =>
        {
            context.Response.StatusCode = status;
            if (selectorDenied) context.Items[TenantRequestItems.SelectorDenied] = true;
            return Task.CompletedTask;
        }, http);
        await response.StartAsync();

        fixture.AssertSingleDenial(status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EarlyTenantBudgetRejectionRecordsOneEvent(bool concurrencyExhausted)
    {
        using var fixture = new Fixture(new TenantResourceOptions
        {
            RequestsPerMinute = concurrencyExhausted ? 10 : 1,
            ConcurrentRequests = concurrencyExhausted ? 1 : 10
        });
        using var consumed = concurrencyExhausted
            ? fixture.Limits.AcquireConcurrency(fixture.Tenant) : fixture.Limits.AcquireRequest(fixture.Tenant);
        var response = new DeferredResponseFeature();
        var http = Request(response);
        var called = false;

        await fixture.Invoke(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, http);
        await response.StartAsync();

        Assert.False(called);
        Assert.Equal(429, http.Response.StatusCode);
        fixture.AssertSingleDenial(429);
    }

    [Fact]
    public async Task PrincipalBudgetRejectionRecordsOneEventAndSuccessfulRequestRecordsNone()
    {
        using var fixture = new Fixture(new TenantResourceOptions { PrincipalRequestsPerMinute = 1 });
        var principal = new TenantPrincipalBudgetMiddleware(context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        RequestDelegate inner = context => principal.InvokeAsync(context, fixture.Limits, fixture.Context);
        var firstResponse = new DeferredResponseFeature();
        var first = Request(firstResponse);
        await fixture.Invoke(inner, first);
        await firstResponse.StartAsync();
        Assert.Equal(204, first.Response.StatusCode);
        Assert.Empty(fixture.Logger.Events);

        var secondResponse = new DeferredResponseFeature();
        var second = Request(secondResponse);
        await fixture.Invoke(inner, second);
        await secondResponse.StartAsync();

        Assert.Equal(429, second.Response.StatusCode);
        fixture.AssertSingleDenial(429);
    }

    [Fact]
    public async Task DeactivatedTenantIsRefusedBeforeTheEndpoint()
    {
        using var fixture = new Fixture(active: false);
        var response = new DeferredResponseFeature();
        var http = Request(response);
        var called = false;

        await fixture.Invoke(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, http);

        Assert.False(called);
        Assert.Equal(503, http.Response.StatusCode);
    }

    private static DefaultHttpContext Request(DeferredResponseFeature response)
    {
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(response);
        http.Request.Path = "/api/v1/workflows";
        return http;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ITenantAvailability _availability;
        private readonly TenantSecuritySignals _signals;
        public TenantInfo Tenant { get; } = TestTenants.Active();
        public ITenantContext Context { get; }
        public CaptureLogger Logger { get; } = new();
        public TenantResourceLimits Limits { get; }

        public Fixture(TenantResourceOptions? limits = null, bool active = true)
        {
            Context = TestTenants.Fixed(Tenant);
            var availability = new Mock<ITenantAvailability>();
            availability.Setup(x => x.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
            _availability = availability.Object;
            Limits = new TenantResourceLimits(Options.Create(limits ?? new TenantResourceOptions()));
            _signals = new TenantSecuritySignals(TimeProvider.System, Logger);
        }

        /// <summary>The production order: process budget, tenant resolution, tenant budget, then <paramref name="inner"/>.</summary>
        public Task Invoke(RequestDelegate inner, HttpContext http)
        {
            var tenantBudget = new TenantRequestBudgetMiddleware(inner);
            var global = new GlobalRequestBudgetMiddleware(context =>
            {
                context.Items[TenantRequestItems.TenantId] = Tenant.Id;
                return tenantBudget.InvokeAsync(context, Limits, Context, _availability);
            });
            return global.InvokeAsync(http, Limits, _signals);
        }

        public void AssertSingleDenial(int status)
        {
            var entry = Assert.Single(Logger.Events);
            Assert.Equal(4201, entry.Id);
            Assert.Contains(Tenant.Id.ToString(), entry.Message);
            Assert.Contains($"StatusCode={status}", entry.Message);
        }

        public void Dispose() => Limits.Dispose();
    }

    private sealed class CaptureLogger : ILogger<TenantSecuritySignals>
    {
        public List<(int Id, string Message)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add((eventId.Id, formatter(state, exception)));
    }

    // DefaultHttpContext has no transport to start the response. Model the server's
    // LIFO OnStarting callbacks explicitly, after the outer handler picks its status.
    private sealed class DeferredResponseFeature : IHttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _starting = new();
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }
        public void OnStarting(Func<object, Task> callback, object state) => _starting.Push((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public async Task StartAsync()
        {
            while (_starting.TryPop(out var registration))
                await registration.Callback(registration.State);
            HasStarted = true;
        }
    }
}
