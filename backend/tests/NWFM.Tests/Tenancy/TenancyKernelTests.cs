using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;

namespace NWFM.Tests.Tenancy;

public sealed class TenancyKernelTests
{
    [Fact]
    public void AFlowBoundToOneTenantCanNeverBeReboundToAnother()
    {
        var context = new TenantContextAccessor();
        var alpha = TestTenants.Active(key: "alpha");
        using var request = context.BeginRequestScope();

        context.SetTenant(alpha);
        context.SetTenant(alpha);

        Assert.Same(alpha, context.Current);
        var refused = Assert.Throws<TenantAccessException>(() => context.SetTenant(TestTenants.Active(key: "beta")));
        Assert.Equal(TenancyErrorCodes.TenantMismatch, refused.Code);
        Assert.Same(alpha, context.Current);
    }

    [Fact]
    public void APlatformScopeCanNeverBeBoundToATenant()
    {
        var context = new TenantContextAccessor();
        using var platform = context.BeginPlatformScope();

        Assert.True(context.IsPlatformScope);
        Assert.Throws<TenantAccessException>(() => context.SetTenant(TestTenants.Active()));
        Assert.Null(context.Current);
    }

    [Fact]
    public void ScopesRestoreThePreviousTenantWhenDisposed()
    {
        var context = new TenantContextAccessor();
        var alpha = TestTenants.Active(key: "alpha");
        var beta = TestTenants.Active(key: "beta");

        using (context.BeginScope(alpha))
        {
            using (context.BeginScope(beta))
                Assert.Same(beta, context.Current);
            Assert.Same(alpha, context.Current);
        }

        Assert.Null(context.Current);
    }

    [Fact]
    public async Task ATenantBoundInsideTheRequestIsVisibleToCodeThatRunsAfterIt()
    {
        var context = new TenantContextAccessor();
        var alpha = TestTenants.Active();
        using var request = context.BeginRequestScope();

        // The bearer handler binds the tenant deep inside authentication; middleware after it reads it.
        await Task.Run(() => context.SetTenant(alpha));

        Assert.Same(alpha, context.Current);
    }

    [Fact]
    public void AmbientCurrentTenantIsEmptyWhenNothingIsBound()
    {
        var context = new TenantContextAccessor();
        Assert.Equal(Guid.Empty, new AmbientCurrentTenant(context).OrganizationId);
        var alpha = TestTenants.Active();
        using var scope = context.BeginScope(alpha);
        Assert.Equal(alpha.Id, new AmbientCurrentTenant(context).OrganizationId);
    }

    [Theory]
    [InlineData(TenantStatus.Active, true, null)]
    [InlineData(TenantStatus.Active, false, TenancyErrorCodes.TenantSubscriptionInactive)]
    [InlineData(TenantStatus.Suspended, true, TenancyErrorCodes.TenantSuspended)]
    [InlineData(TenantStatus.Maintenance, true, TenancyErrorCodes.TenantMaintenance)]
    [InlineData(TenantStatus.Provisioning, true, TenancyErrorCodes.TenantNotFound)]
    [InlineData(TenantStatus.Deleted, true, TenancyErrorCodes.TenantNotFound)]
    public void OnlyAnActiveTenantWithAUsableSubscriptionServes(TenantStatus status, bool usable, string? refusal)
    {
        var tenant = new TenantInfo { Id = Guid.NewGuid(), Key = "alpha", Name = "Alpha", Status = status, IsSubscriptionUsable = usable };
        Assert.Equal(refusal, TenantAccessException.ForStatus(tenant)?.Code);
        Assert.Equal(refusal is null, tenant.IsServing);
    }

    [Fact]
    public void SchemaIsBehindWhenAnyModuleHasANewerMigrationThanRecorded()
    {
        ITenantDatabaseModule[] modules = [Module("Auth", "20260101_A"), Module("Workflow", "20260301_W"), Module("Seed", null)];
        var current = TenantSchema.Compose(modules);

        Assert.Equal("Auth=20260101_A;Workflow=20260301_W", current);
        Assert.False(TenantSchema.IsBehind(current, modules));
        Assert.True(TenantSchema.IsBehind("Auth=20260101_A;Workflow=20260201_W", modules));
        Assert.True(TenantSchema.IsBehind("Auth=20260101_A", modules));
        Assert.True(TenantSchema.IsBehind(null, modules));
        // A database ahead of this build is served: migrations are additive and a rollback must work.
        Assert.False(TenantSchema.IsBehind("Auth=20270101_A;Workflow=20270101_W", modules));
    }

    [Fact]
    public void RefreshTokenPrefixNamesItsTenant()
    {
        var tenant = Guid.NewGuid();
        Assert.True(TenantRefreshTokens.TryReadTenantId(TenantRefreshTokens.Compose(tenant, "random"), out var read));
        Assert.Equal(tenant, read);
        Assert.False(TenantRefreshTokens.TryReadTenantId("no-prefix", out _));
        Assert.False(TenantRefreshTokens.TryReadTenantId(null, out _));
    }

    [Theory]
    [InlineData("alpha", true)]
    [InlineData("../escape", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/b", false)]
    [InlineData(" alpha", false)]
    public void OnlyASingleSafeFolderNameCanBecomeAStorageRoot(string key, bool safe) =>
        Assert.Equal(safe, TenantStorageLayout.IsSafeKey(key));

    [Fact]
    public async Task FanOutRunsEachServingTenantInItsOwnScopeAndIsolatesFailures()
    {
        var context = new TenantContextAccessor();
        var alpha = TestTenants.Active(key: "alpha");
        var beta = TestTenants.Active(key: "beta");
        var suspended = TestTenants.WithStatus(TenantStatus.Suspended, key: "gamma");
        var store = new Mock<ITenantStore>();
        store.Setup(s => s.GetAll()).Returns([alpha, beta, suspended]);
        var fanOut = new TenantFanOut(store.Object, context, [], Options.Create(new TenancyOptions { WorkerParallelism = 1 }),
            NullLogger<TenantFanOut>.Instance);
        var seen = new List<Guid>();

        await fanOut.ForEachServingTenantAsync((tenant, _) =>
        {
            Assert.Same(tenant, context.Current);
            seen.Add(tenant.Id);
            if (tenant == alpha) throw new InvalidOperationException("alpha is broken");
            return Task.CompletedTask;
        }, default);

        Assert.Equal([alpha.Id, beta.Id], seen);
        Assert.Null(context.Current);
    }

    [Fact]
    public async Task FanOutSkipsATenantThatFailsAReadinessGate()
    {
        var context = new TenantContextAccessor();
        var ready = TestTenants.Active(key: "ready");
        var behind = TestTenants.Active(key: "behind");
        var store = new Mock<ITenantStore>();
        store.Setup(s => s.GetAll()).Returns([ready, behind]);
        var gate = new Mock<ITenantReadiness>();
        gate.Setup(g => g.CheckAsync(It.IsAny<TenantInfo>(), It.IsAny<CancellationToken>()))
            .Returns<TenantInfo, CancellationToken>((tenant, _) =>
                ValueTask.FromResult(tenant == behind ? TenantAccessException.SchemaUpgradePending() : null));
        var fanOut = new TenantFanOut(store.Object, context, [gate.Object], Options.Create(new TenancyOptions()),
            NullLogger<TenantFanOut>.Instance);
        var seen = new List<string>();

        await fanOut.ForEachServingTenantAsync((tenant, _) => { lock (seen) seen.Add(tenant.Key); return Task.CompletedTask; }, default);

        Assert.Equal(["ready"], seen);
    }

    private static ITenantDatabaseModule Module(string name, string? latest)
    {
        var module = new Mock<ITenantDatabaseModule>();
        module.SetupGet(m => m.Name).Returns(name);
        module.SetupGet(m => m.LatestMigrationId).Returns(latest);
        return module.Object;
    }
}
