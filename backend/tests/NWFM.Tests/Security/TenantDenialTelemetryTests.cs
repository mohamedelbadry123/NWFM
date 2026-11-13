using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Api.Hosting;
using NWFM.Api.Services;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Options;

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
        var middleware = new TenantRequestBudgetMiddleware(_ => status == 403
            ? throw new UnauthorizedAccessException("private failure")
            : throw new WorkflowQueueCapacityException("private queue"));

        // The production exception handler wraps the budget middleware. It clears
        // the response and chooses the status after the budget InvokeAsync unwinds.
        var exception = await Record.ExceptionAsync(() => fixture.Invoke(middleware, http));
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
        var middleware = new TenantRequestBudgetMiddleware(context =>
        {
            context.Response.StatusCode = status;
            if (selectorDenied) context.Items["TenantSecurity.SelectorDenied"] = true;
            return Task.CompletedTask;
        });

        await fixture.Invoke(middleware, http);
        await response.StartAsync();

        fixture.AssertSingleDenial(status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EarlyGlobalBudgetRejectionRecordsOneEvent(bool concurrencyExhausted)
    {
        using var fixture = new Fixture(new TenantResourceOptions
        {
            RequestsPerMinute = concurrencyExhausted ? 10 : 1,
            ConcurrentRequests = concurrencyExhausted ? 1 : 10
        });
        using var consumed = concurrencyExhausted
            ? fixture.Limits.AcquireConcurrency() : fixture.Limits.AcquireRequest();
        var response = new DeferredResponseFeature();
        var http = Request(response);
        var called = false;
        var middleware = new TenantRequestBudgetMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await fixture.Invoke(middleware, http);
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
        var middleware = new TenantRequestBudgetMiddleware(context => principal.InvokeAsync(context, fixture.Limits));
        var firstResponse = new DeferredResponseFeature();
        var first = Request(firstResponse);
        await fixture.Invoke(middleware, first);
        await firstResponse.StartAsync();
        Assert.Equal(204, first.Response.StatusCode);
        Assert.Empty(fixture.Logger.Events);

        var secondResponse = new DeferredResponseFeature();
        var second = Request(secondResponse);
        await fixture.Invoke(middleware, second);
        await secondResponse.StartAsync();

        Assert.Equal(429, second.Response.StatusCode);
        fixture.AssertSingleDenial(429);
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
        private readonly IOptions<ApplicationOptions> _application = Options.Create(
            new ApplicationOptions { TenantId = Guid.NewGuid() });
        private readonly IOptionsMonitor<TenantIsolationOptions> _tenant;
        private readonly ITenantAvailability _availability;
        private readonly TenantSecuritySignals _signals;
        public CaptureLogger Logger { get; } = new();
        public TenantResourceLimits Limits { get; }

        public Fixture(TenantResourceOptions? limits = null)
        {
            var tenant = new Mock<IOptionsMonitor<TenantIsolationOptions>>();
            tenant.SetupGet(x => x.CurrentValue).Returns(new TenantIsolationOptions { IsActive = true });
            _tenant = tenant.Object;
            var availability = new Mock<ITenantAvailability>();
            availability.Setup(x => x.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _availability = availability.Object;
            Limits = new TenantResourceLimits(Options.Create(limits ?? new TenantResourceOptions()), _application);
            _signals = new TenantSecuritySignals(_application, TimeProvider.System, Logger);
        }

        public Task Invoke(TenantRequestBudgetMiddleware middleware, HttpContext http) =>
            middleware.InvokeAsync(http, Limits, _application, _tenant, _availability, _signals,
                NullLogger<TenantRequestBudgetMiddleware>.Instance);

        public void AssertSingleDenial(int status)
        {
            var entry = Assert.Single(Logger.Events);
            Assert.Equal(4201, entry.Id);
            Assert.Contains(_application.Value.TenantId.ToString(), entry.Message);
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
