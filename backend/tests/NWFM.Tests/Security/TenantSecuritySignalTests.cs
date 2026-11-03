using Microsoft.Extensions.Logging;
using NWFM.Api.Hosting;

namespace NWFM.Tests.Security;

public sealed class TenantSecuritySignalTests
{
    [Fact]
    public void DenialBurstEmitsOneTenantBoundAlertAndRearmsAfterWindow()
    {
        var tenant = Guid.NewGuid();
        var clock = new Clock();
        var logger = new CaptureLogger();
        var monitor = new TenantSecuritySignals(clock, logger);
        for (var i = 0; i < 100; i++) monitor.Denied(tenant, 403);
        Assert.Single(logger.Events, e => e.Id == 4202);
        Assert.All(logger.Events, e => Assert.Contains(tenant.ToString(), e.Message));
        clock.Now = clock.Now.AddMinutes(2);
        monitor.Denied(tenant, 403);
        Assert.Single(logger.Events, e => e.Id == 4202);
        for (var i = 0; i < 19; i++) monitor.Denied(tenant, 401);
        Assert.Equal(2, logger.Events.Count(e => e.Id == 4202));
    }

    [Fact]
    public void OneTenantsDenialsNeverRaiseAnotherTenantsAlert()
    {
        var logger = new CaptureLogger();
        var monitor = new TenantSecuritySignals(new Clock(), logger);
        var noisy = Guid.NewGuid();
        var quiet = Guid.NewGuid();
        for (var i = 0; i < 19; i++) monitor.Denied(noisy, 403);
        for (var i = 0; i < 19; i++) monitor.Denied(quiet, 403);
        Assert.DoesNotContain(logger.Events, e => e.Id == 4202);
        monitor.Denied(noisy, 403);
        var alert = Assert.Single(logger.Events, e => e.Id == 4202);
        Assert.Contains(noisy.ToString(), alert.Message);
    }

    [Fact]
    public void RefusedResolutionIsRecordedAgainstTheUnresolvedBucket()
    {
        var logger = new CaptureLogger();
        var monitor = new TenantSecuritySignals(new Clock(), logger);
        for (var i = 0; i < 20; i++) monitor.ResolutionRefused(Guid.Empty, "ghost", "Tenant.NotFound");
        Assert.Equal(20, logger.Events.Count(e => e.Id == 4203));
        var alert = Assert.Single(logger.Events, e => e.Id == 4202);
        Assert.Contains("unresolved", alert.Message);
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
