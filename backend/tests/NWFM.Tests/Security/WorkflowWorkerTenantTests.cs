using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;
using Workflow.Application.Abstractions;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Background;

namespace NWFM.Tests.Security;

public sealed class WorkflowWorkerTenantTests
{
    [Fact]
    public async Task InactiveTenantDesignerRecoveryDoesNotResolveRuntimeOrReadQueues()
    {
        var availability = new Mock<ITenantAvailability>();
        availability.Setup(a => a.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await using var services = new ServiceCollection()
            .Configure<TenantIsolationOptions>(o => o.TenantId = Guid.NewGuid())
            .AddSingleton(availability.Object).BuildServiceProvider();
        // No runtime or database is registered: either recovery dispatch would fail.
        var worker = new WorkflowIntegrationHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowIntegrationHostedService>.Instance);
        await worker.ProcessDesignerRecoveryAsync(default);
        availability.Verify(a => a.IsActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void EveryHostedWorkerRequiresExplicitTenantBoundaryClassification()
    {
        var assemblies = new[]
        {
            typeof(WorkflowInboxHostedService).Assembly,
            typeof(global::Tasks.Infrastructure.C2m.C2mClosureHostedService).Assembly,
            typeof(global::Auth.Infrastructure.Persistence.AuthDbContext).Assembly,
            typeof(global::FormEngine.Infrastructure.DependencyInjection).Assembly,
            typeof(NWFM.Api.Controllers.AppContextController).Assembly
        };
        var discovered = assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract &&
                typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type))
            .Select(type => type.Name).Order().ToArray();
        string[] classified =
        [
            "C2mClosureHostedService", "WorkflowInboxHostedService",
            "WorkflowIntegrationHostedService", "WorkflowOutboxHostedService",
            "WorkflowTimerHostedService"
        ];
        Assert.Equal(classified.Order(), discovered);
    }

    [Fact]
    public async Task OffboardingAfterBatchReadPreservesUndeliveredMessages()
    {
        var tenant = Guid.NewGuid();
        var message = WorkflowIntegrationInbox.Create(tenant, "message", "idempotency",
            "module", "entity", "1", "trigger", "{}", DateTime.UtcNow, bindingId: Guid.NewGuid());
        var inbox = new Mock<IWorkflowIntegrationInboxRepository>(MockBehavior.Strict);
        inbox.Setup(i => i.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { message });
        var availability = new Mock<ITenantAvailability>();
        availability.SetupSequence(a => a.IsActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        var engine = new Mock<IWorkflowRuntimeEngine>(MockBehavior.Strict);
        var instances = new Mock<IWorkflowInstanceRepository>(MockBehavior.Strict);
        await using var services = new ServiceCollection()
            .Configure<TenantIsolationOptions>(o => o.TenantId = tenant)
            .AddSingleton(availability.Object)
            .AddSingleton(inbox.Object)
            .AddSingleton(engine.Object)
            .AddSingleton(instances.Object)
            .BuildServiceProvider();
        var worker = new WorkflowInboxHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowInboxHostedService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => worker.ProcessPendingBatchAsync(default));

        Assert.Equal(WorkflowInboxStatus.Pending, message.Status);
        Assert.Equal(0, message.AttemptCount);
        inbox.Verify(i => i.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        inbox.VerifyNoOtherCalls();
        engine.VerifyNoOtherCalls();
        instances.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InactiveTenantWorkerDoesNotEvenReadTheQueue()
    {
        var inbox = new Mock<IWorkflowIntegrationInboxRepository>(MockBehavior.Strict);
        await using var services = new ServiceCollection()
            .Configure<TenantIsolationOptions>(o => { o.TenantId = Guid.NewGuid(); o.IsActive = false; })
            .AddSingleton(inbox.Object)
            .BuildServiceProvider();
        var worker = new WorkflowInboxHostedService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowInboxHostedService>.Instance);
        await worker.ProcessPendingBatchAsync(default);
        inbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConsumerRejectsMissingInactiveAndForeignTenantContext()
    {
        var tenant = Guid.NewGuid();
        using var missing = new ServiceCollection().BuildServiceProvider();
        Assert.False(await WorkflowTenantAdmission.IsActiveAsync(missing, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkflowTenantAdmission.RequireMessageScopeAsync(missing, tenant, default));
        var availability = new Mock<ITenantAvailability>();
        availability.Setup(a => a.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var active = new ServiceCollection().Configure<TenantIsolationOptions>(o => o.TenantId = tenant)
            .AddSingleton(availability.Object).BuildServiceProvider();
        Assert.True(await WorkflowTenantAdmission.IsActiveAsync(active, default));
        await WorkflowTenantAdmission.RequireMessageScopeAsync(active, tenant, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkflowTenantAdmission.RequireMessageScopeAsync(active, Guid.NewGuid(), default));
        using var inactive = new ServiceCollection()
            .Configure<TenantIsolationOptions>(o => { o.TenantId = tenant; o.IsActive = false; }).BuildServiceProvider();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkflowTenantAdmission.RequireMessageScopeAsync(inactive, tenant, default));
        availability.Setup(a => a.IsActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.False(await WorkflowTenantAdmission.IsActiveAsync(active, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkflowTenantAdmission.RequireMessageScopeAsync(active, tenant, default));
    }
}
