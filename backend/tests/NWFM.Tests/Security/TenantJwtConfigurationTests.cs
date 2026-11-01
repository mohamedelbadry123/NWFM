using Auth.Domain.Options;
using Auth.Infrastructure;
using Auth.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace NWFM.Tests.Security;

public sealed class TenantJwtConfigurationTests
{
    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "short-secret")]
    [InlineData("Production", "REPLACE_WITH_USER_SECRET_OR_ENV_VAR_min_32_chars")]
    [InlineData("Development", "REPLACE_WITH_USER_SECRET_OR_ENV_VAR_min_32_chars")]
    [InlineData("Production", "NWFM-dev-signing-key-minimum-32-chars-long!")]
    [InlineData("Staging", "NWFM-dev-signing-key-minimum-32-chars-long!")]
    public void InsecureKeyStopsAuthRegistration(string environment, string key)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = environment
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SigningKey"] = key
        });

        var error = Assert.Throws<OptionsValidationException>(() =>
            builder.AddAuthInfrastructure());

        Assert.Contains("at least 32 UTF-8 bytes", error.Message);
        if (key.Length != 0) Assert.DoesNotContain(key, error.Message);
    }

    [Theory]
    [InlineData("Development", "NWFM-dev-signing-key-minimum-32-chars-long!")]
    [InlineData("Production", "a-long-test-only-secret-with-at-least-32-bytes")]
    public void ExplicitKeyOrDevelopmentOnlyDefaultIsAccepted(string environment, string key)
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(x => x.EnvironmentName).Returns(environment);
        var validator = new JwtSettingsValidator(host.Object);

        Assert.True(validator.Validate(Options.DefaultName, new JwtSettings { SigningKey = key }).Succeeded);
    }
}
