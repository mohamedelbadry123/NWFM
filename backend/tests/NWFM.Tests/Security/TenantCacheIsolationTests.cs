using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Options;

namespace NWFM.Tests.Security;

public sealed class TenantCacheIsolationTests
{
    [Fact]
    public async Task SharedCacheSeparatesReadsWritesAndInvalidationForIdenticalLogicalKeys()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var shared = provider.GetRequiredService<HybridCache>();
        var cacheA = Create(shared, Guid.NewGuid());
        var cacheB = Create(shared, Guid.NewGuid());
        var keys = new[]
        {
            CacheKeys.Auth.Permissions.ForUser("same-user"),
            CacheKeys.Auth.Otp.ForChallenge("same-challenge"),
            CacheKeys.Lookups.OrgHierarchy,
            CacheKeys.FormEngine.FieldCatalog,
            CacheKeys.Tasks.C2mActionMappings
        };
        foreach (var key in keys)
        {
            await cacheA.SetAsync(key, "A-secret");
            await cacheB.SetAsync(key, "B-secret");
            Assert.Equal("A-secret", await Read(cacheA, key));
            Assert.Equal("B-secret", await Read(cacheB, key));
        }
        await cacheA.RemoveAsync(keys[0]);
        await cacheA.RemoveAsync(keys.Skip(1));
        foreach (var key in keys)
        {
            Assert.Equal("missing", await Read(cacheA, key));
            Assert.Equal("B-secret", await Read(cacheB, key));
        }
    }

    [Fact]
    public void CacheWithoutServerTenantFailsClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => Create(provider.GetRequiredService<HybridCache>(), Guid.Empty));
    }

    [Fact]
    public async Task SameTenantInDifferentEnvironmentsCannotShareCachedCredentials()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var shared = provider.GetRequiredService<HybridCache>();
        var tenant = Guid.NewGuid();
        var production = Create(shared, tenant, "Production");
        var development = Create(shared, tenant, "Development");
        var key = CacheKeys.Auth.Otp.ForChallenge("challenge");
        await production.SetAsync(key, "production-otp");
        Assert.Equal("missing", await Read(development, key));
    }

    private static HybridCacheService Create(HybridCache cache, Guid tenantId, string environment = "Testing") =>
        new(cache, Options.Create(new CacheSettings()),
            Options.Create(new TenantIsolationOptions { TenantId = tenantId }),
            Mock.Of<IHostEnvironment>(x => x.EnvironmentName == environment));

    private static ValueTask<string> Read(ICacheService cache, string key) =>
        cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("missing"));
}
