using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Exceptions;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;
using Tenancy.Domain.Entities;
using Tenancy.Infrastructure.MultiTenancy;
using Tenancy.Infrastructure.Persistence;
using CatalogTenant = Tenancy.Domain.Entities.Tenant;

namespace NWFM.Tests.Tenancy;

public sealed class CatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnAdoptedTenantKeepsTheIdItsDatabaseIsBoundTo()
    {
        var id = Guid.NewGuid();
        var tenant = CatalogTenant.Create("nwfm", "NWFM", "Admin", "admin@localhost", null, TenantSeedProfile.Bootstrap, Now, id);
        Assert.Equal(id, tenant.Id);
        Assert.Throws<DomainException>(() =>
            CatalogTenant.Create("nwfm", "NWFM", "Admin", "admin@localhost", null, TenantSeedProfile.Bootstrap, Now, Guid.Empty));
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-water", true)]
    [InlineData("ab", false)]
    [InlineData("1acme", false)]
    [InlineData("acme--water", false)]
    [InlineData("acme-", false)]
    [InlineData("acme.water", false)]
    public void ATenantKeyIsASingleDnsLabel(string key, bool valid) => Assert.Equal(valid, TenantKey.IsValid(key));

    [Fact]
    public void AProvisionedDatabaseCarriesSeparateOwnerAndRuntimeCredentials()
    {
        var tenantId = Guid.NewGuid();
        var database = TenantDatabase.CreateManaged(tenantId, 1, "nwfm_t_acme", "nwfm_t_acme_owner", "nwfm_t_acme_app", "runtime", "owner");
        Assert.Equal("runtime", database.EncryptedConnectionString);
        Assert.Equal("owner", database.EncryptedAdminConnectionString);
        Assert.Equal("nwfm_t_acme_app", database.RuntimePrincipal);

        database.RecordVerification(Now);
        database.ReplaceConnectionStrings("runtime2", "owner2");
        Assert.Null(database.VerifiedAtUtc);

        Assert.Throws<DomainException>(() => TenantDatabase.CreateManaged(tenantId, 1, "db", "owner", "app", "runtime", ""));
    }

    [Fact]
    public void ABringYourOwnSignupNeedsADistinctRuntimePrincipalBeforeItCanBeQueued()
    {
        var signup = Signup(TenantDatabaseMode.BringYourOwn, approvalRequired: false);
        Assert.True(signup.VerifyEmail("hash", Now));
        Assert.Throws<DomainException>(() => signup.Queue(Now));

        Assert.Throws<DomainException>(() => signup.SetBringYourOwnDatabase(
            "db.example.com", null, "acme", "owner", "secret", "OWNER", "secret2", false, Now));

        signup.SetBringYourOwnDatabase("db.example.com", null, "acme", "owner", "secret", "runtime", "secret2", false, Now);
        Assert.True(signup.HasBringYourOwnDatabase);
        signup.Queue(Now);
        Assert.Equal(SignupStatus.Queued, signup.Status);
    }

    [Fact]
    public void ASelfServiceSignupWaitsForStaffApprovalBeforeAnythingIsCreated()
    {
        var signup = Signup(TenantDatabaseMode.Managed, approvalRequired: true);
        Assert.True(signup.VerifyEmail("hash", Now));
        Assert.Throws<DomainException>(() => signup.Queue(Now));

        signup.AwaitApproval(Now);
        Assert.Equal(SignupStatus.AwaitingApproval, signup.Status);
        signup.Approve("platform@localhost", Now);
        Assert.Equal(SignupStatus.Queued, signup.Status);
    }

    [Fact]
    public void AWrongVerificationTokenNeverConfirmsTheEmail()
    {
        var signup = Signup(TenantDatabaseMode.Managed, approvalRequired: true);
        for (var i = 0; i < 10; i++) Assert.False(signup.VerifyEmail("wrong", Now));
        // Exhausted attempts lock even the right token out.
        Assert.False(signup.VerifyEmail("hash", Now));
        Assert.False(signup.IsEmailVerified);
    }

    [Fact]
    public void TheStoreResolvesRegisteredHostsSubdomainsAndNeverReservedLabels()
    {
        using var catalog = Catalog(out var store, rootDomain: "localhost");
        var alpha = AddTenant(catalog, "alpha", "alpha.example.com");
        AddTenant(catalog, "beta");
        var www = AddTenant(catalog, "www-corp");

        Assert.Equal(alpha.Id, store.FindByHost("ALPHA.example.com")?.Id);
        Assert.Equal("beta", store.FindByHost("beta.localhost")?.Key);
        Assert.Null(store.FindByHost("www.localhost"));
        Assert.Null(store.FindByHost("deep.beta.localhost"));
        Assert.Null(store.FindByHost("beta.evil.com"));
        Assert.Null(store.FindByHost("unknown.localhost"));
        Assert.Equal(www.Id, store.FindByKey("WWW-CORP")?.Id);
    }

    [Fact]
    public void TheStoreHidesDeletedTenantsAndReportsSubscriptionUsability()
    {
        using var catalog = Catalog(out var store);
        var gone = AddTenant(catalog, "gone");
        gone.MarkDeleted("offboarded", Now);
        catalog.SaveChanges();
        store.Invalidate();

        Assert.Null(store.FindByKey("gone"));
        Assert.All(store.GetAll(), tenant => Assert.True(tenant.IsSubscriptionUsable));
    }

    [Fact]
    public void TheResolverDecryptsOnlyTheCredentialTheHostIsEntitledTo()
    {
        var protector = new Mock<ISecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((purpose, value) => $"{purpose}|{value}");

        using var runtimeCatalog = Catalog(out var runtimeStore, role: TenantConnectionRole.Runtime);
        var runtimeInfo = runtimeStore.FindById(AddTenant(runtimeCatalog, "alpha").Id)!;
        var runtime = Resolver(runtimeStore, protector.Object, TenantConnectionRole.Runtime).Resolve(runtimeInfo);
        Assert.Equal($"{SecretPurposes.TenantRuntimeConnection}|runtime-cipher", runtime);
        // The tenant API's store never even loads the owner credential.
        Assert.Throws<InvalidOperationException>(() =>
            Resolver(runtimeStore, protector.Object, TenantConnectionRole.Administration).Resolve(runtimeInfo));

        using var adminCatalog = Catalog(out var adminStore, role: TenantConnectionRole.Administration);
        var adminInfo = adminStore.FindById(AddTenant(adminCatalog, "alpha").Id)!;
        var owner = Resolver(adminStore, protector.Object, TenantConnectionRole.Administration).Resolve(adminInfo);
        Assert.Equal($"{SecretPurposes.TenantAdminConnection}|owner-cipher", owner);
    }

    [Fact]
    public void TheResolverFailsClosedWithoutABoundTenant()
    {
        using var catalog = Catalog(out var store);
        var resolver = Resolver(store, Mock.Of<ISecretProtector>(), TenantConnectionRole.Runtime);
        var refusal = Assert.Throws<TenantAccessException>(() => resolver.ResolveForCurrentTenant());
        Assert.Equal(TenancyErrorCodes.TenantRequired, refusal.Code);
    }

    private static TenantConnectionStringResolver Resolver(CatalogTenantStore store, ISecretProtector protector, TenantConnectionRole role) =>
        new(new TenantContextAccessor(), store, protector,
            Options.Create(new TenancyOptions { ConnectionRole = role }), new ConfigurationBuilder().Build());

    private static SignupRequest Signup(TenantDatabaseMode mode, bool approvalRequired) =>
        SignupRequest.Create("acme", "Acme Water", "Owner", "owner@acme.test", null, "trial", "en", mode,
            "hash", Now.AddDays(2), "203.0.113.5", approvalRequired, Now);

    private static CatalogDbContext Catalog(
        out CatalogTenantStore store, string rootDomain = "", TenantConnectionRole role = TenantConnectionRole.Runtime)
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<CatalogDbContext>(o => o.UseInMemoryDatabase(name))
            .BuildServiceProvider();
        store = new CatalogTenantStore(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenancyOptions { RootDomain = rootDomain, ConnectionRole = role }), TimeProvider.System,
            NullLogger<CatalogTenantStore>.Instance);
        var context = services.CreateScope().ServiceProvider.GetRequiredService<CatalogDbContext>();
        context.Plans.Add(Plan.Create("enterprise", "Enterprise", "مؤسسات", null, null, null, null, null, 0, false, 1));
        context.SaveChanges();
        return context;
    }

    private static CatalogTenant AddTenant(CatalogDbContext catalog, string key, params string[] hosts)
    {
        var tenant = CatalogTenant.Create(key, key, "Owner", $"owner@{key}.test", null, TenantSeedProfile.Standard, Now);
        foreach (var host in hosts) tenant.AddHost(host);
        tenant.AttachDatabase(TenantDatabase.CreateManaged(tenant.Id, 1, $"nwfm_t_{key}", "owner", "app", "runtime-cipher", "owner-cipher"));
        tenant.StartSubscription(Subscription.Start(tenant.Id, catalog.Plans.First(), Now));
        tenant.Activate(Now);
        catalog.Tenants.Add(tenant);
        catalog.SaveChanges();
        return tenant;
    }
}
