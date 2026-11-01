using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;
using NWFM.Tests.Tenancy;
using Tasks.Application.C2m;
using Tasks.Infrastructure.C2m;

namespace NWFM.Tests.Security;

public sealed class TaskWorkerTenantTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InactiveTenantDoesNotReadOrSendQueuedC2mClosures(bool configuredActive, bool durableActive)
    {
        var availability = new Mock<ITenantAvailability>();
        availability.Setup(a => a.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(durableActive);
        await using var services = new ServiceCollection()
            .AddSingleton(TestTenants.Fixed(configuredActive ? TestTenants.Active(Guid.NewGuid()) : TestTenants.WithStatus(TenantStatus.Suspended, Guid.NewGuid())))
            .AddSingleton(availability.Object)
            // No database or transport registered: reaching either is a test failure.
            .BuildServiceProvider();
        var worker = new C2mClosureHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new C2mOptions()), TimeProvider.System, NullLogger<C2mClosureHostedService>.Instance);

        await worker.SendQueuedAsync(TimeSpan.FromSeconds(30), default);
        // Also covers a tenant suspended after its queued IDs have already been read.
        await worker.SendOneAsync(Guid.NewGuid(), DateTime.UtcNow, default);
    }

    [Fact]
    public async Task MissingTenantContextCannotDispatchC2mClosures()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new C2mClosureHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new C2mOptions()), TimeProvider.System, NullLogger<C2mClosureHostedService>.Instance);
        await worker.SendQueuedAsync(TimeSpan.FromSeconds(30), default);
        await worker.SendOneAsync(Guid.NewGuid(), DateTime.UtcNow, default);
    }
}
