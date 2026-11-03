using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Platform.Application.Common.Interfaces;
using Platform.Infrastructure.Provisioning;
using Tenancy.Domain.Constants;

namespace NWFM.Tests.Platform;

/// <summary>
/// The control plane's HTTP surface: every action needs platform staff (or an administrator for a
/// change) except the reviewed public signup entry points, and the two hosts never share a controller.
/// </summary>
public sealed class PlatformBoundaryTests
{
    private static readonly Dictionary<string, string> AnonymousActions = new()
    {
        ["PlatformAuthController.Login"] = "Platform staff sign-in; rate limited and locked out after repeated failures.",
        ["PublicSignupController.GetOptions"] = "Public plans and captcha site key for the signup form.",
        ["PublicSignupController.CheckKey"] = "Reports only whether a workspace address is free.",
        ["PublicSignupController.Submit"] = "Creates an unverified request; captcha and per-address limits apply.",
        ["PublicSignupController.GetStatus"] = "Progress of one request, addressed by its random id.",
        ["PublicSignupController.Verify"] = "Consumes the e-mailed single-use token.",
        ["PublicSignupController.Resend"] = "Re-sends to the address on record only, throttled per request.",
        ["PublicSignupController.GetDatabaseScript"] = "Static T-SQL template built from validated identifiers.",
        ["PublicSignupController.TestDatabase"] = "Requires the request's e-mailed token; SSRF-screened host.",
        ["PublicSignupController.SaveDatabase"] = "Requires the request's e-mailed token; SSRF-screened host.",
        ["PublicCatalogController.GetWorkspaces"] = "Only workspaces that opted in to being listed."
    };

    [Fact]
    public void EveryPlatformActionNeedsStaffUnlessItIsAReviewedPublicEntryPoint()
    {
        var controllers = typeof(global::Platform.Api.PlatformRateLimits).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)).ToArray();
        Assert.NotEmpty(controllers);

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in controllers)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName))
            {
                var name = $"{controller.Name}.{method.Name}";
                Assert.True(method.GetCustomAttributes<HttpMethodAttribute>(true).Any(), $"{name} must declare its HTTP method.");
                if (controller.IsDefined(typeof(AllowAnonymousAttribute)) || method.IsDefined(typeof(AllowAnonymousAttribute)))
                {
                    Assert.True(AnonymousActions.ContainsKey(name), $"Unreviewed anonymous platform action: {name}");
                    observed.Add(name);
                    continue;
                }

                var policies = controller.GetCustomAttributes<AuthorizeAttribute>().Concat(method.GetCustomAttributes<AuthorizeAttribute>())
                    .Select(a => a.Policy).ToArray();
                Assert.True(policies.Any(p => p is PlatformPolicies.Staff or PlatformPolicies.Admin),
                    $"{name} is not behind a platform policy.");
                if (method.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Any(m => m is "POST" or "PUT" or "DELETE"))
                    && controller.Name is not ("PlatformSupportController" or "PlatformAuthController"))
                    Assert.Contains(PlatformPolicies.Admin, policies);
            }
        }

        Assert.Equal(AnonymousActions.Keys.Order(), observed.Order());
    }

    [Fact]
    public void TheTenantApiAndTheControlPlaneNeverShareAnAssembly()
    {
        var tenantApi = typeof(NWFM.Api.Controllers.AppContextController).Assembly;
        var references = tenantApi.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(references, name => name!.StartsWith("Platform.", StringComparison.Ordinal));

        string[] modules = ["Auth", "FormEngine", "Tasks", "Workflow"];
        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Api" })
            foreach (var module in modules)
            {
                var assembly = Assembly.Load($"{module}.{layer}");
                Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a =>
                    a.Name!.StartsWith("Tenancy.", StringComparison.Ordinal) || a.Name!.StartsWith("Platform.", StringComparison.Ordinal));
            }
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.1")]
    [InlineData("192.168.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.5")]
    public void ABringYourOwnHostOnAPrivateAddressIsRefused(string address) =>
        Assert.True(ByoDatabaseInspector.IsNonPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("8.8.8.8")]
    public void APublicAddressPassesTheHostScreen(string address) =>
        Assert.False(ByoDatabaseInspector.IsNonPublic(IPAddress.Parse(address)));

    [Fact]
    public void TheOwnerAndRuntimeConnectionsUseTheirOwnPrincipals()
    {
        var inspector = new ByoDatabaseInspector(
            Microsoft.Extensions.Options.Options.Create(new NWFM.Shared.Options.PlatformOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ByoDatabaseInspector>.Instance);
        var details = new ByoDatabaseDetails("db.example.com", 1433, "acme", "acme_owner", "o-secret", "acme_app", "r-secret", false);

        var owner = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(inspector.BuildConnectionString(details));
        var runtime = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(inspector.BuildRuntimeConnectionString(details));

        Assert.Equal(("acme_owner", "o-secret"), (owner.UserID, owner.Password));
        Assert.Equal(("acme_app", "r-secret"), (runtime.UserID, runtime.Password));
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory, runtime.Encrypt);
    }

    [Theory]
    [InlineData("db.example.com", "acme", "owner", "app", null)]
    [InlineData("db.example.com;Integrated Security=true", "acme", "owner", "app", "host")]
    [InlineData("db.example.com", "acme;drop", "owner", "app", "database")]
    [InlineData("db.example.com", "acme", "owner", "OWNER", "runtime")]
    [InlineData("db.example.com", "acme", "owner", "app]--", "runtime")]
    public void BringYourOwnFieldsAreValidatedAsSeparateSafeValues(string server, string database, string owner, string runtime, string? fails)
    {
        var error = ByoDatabaseInspector.ValidateFields(new ByoDatabaseDetails(server, null, database, owner, "secret", runtime, "secret2", false));
        if (fails is null) Assert.Null(error);
        else Assert.Contains(fails, error!, StringComparison.OrdinalIgnoreCase);
    }
}
