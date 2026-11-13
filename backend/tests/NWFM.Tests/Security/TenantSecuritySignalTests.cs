using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Api.Hosting;
using NWFM.Api.Services;

namespace NWFM.Tests.Security;

public sealed class TenantSecuritySignalTests
{
    [Fact]
    public void DenialBurstEmitsOneTenantBoundAlertAndRearmsAfterWindow()
    {
        var tenant = Guid.NewGuid();
        var clock = new Clock();
        var logger = new CaptureLogger();
        var monitor = new TenantSecuritySignals(Options.Create(new ApplicationOptions { TenantId = tenant }), clock, logger);
        for (var i = 0; i < 100; i++) monitor.Denied(403);
        Assert.Single(logger.Events, e => e.Id == 4202);
        Assert.All(logger.Events, e => Assert.Contains(tenant.ToString(), e.Message));
        clock.Now = clock.Now.AddMinutes(2);
        monitor.Denied(403);
        Assert.Single(logger.Events, e => e.Id == 4202);
        for (var i = 0; i < 19; i++) monitor.Denied(401);
        Assert.Equal(2, logger.Events.Count(e => e.Id == 4202));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CaptureLogger : ILogger<TenantSecuritySignals>
    {
        public List<(int Id, string Message)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add((eventId.Id, formatter(state, exception)));
    }
}
