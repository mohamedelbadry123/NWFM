using System.Security.Cryptography;
using System.Text;
using Auth.Domain.Options;
using Auth.Infrastructure.Services.Messaging;
using Microsoft.Extensions.Logging;
using Moq;

namespace NWFM.Tests.Security;

public sealed class TenantServiceCredentialTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("foreign-tenant", false)]
    [InlineData("foreign-environment", false)]
    [InlineData("missing-scope", false)]
    [InlineData("disabled", false)]
    [InlineData("wrong-key", false)]
    [InlineData("blank-key", false)]
    [InlineData("malformed-hash", false)]
    public void CredentialsRequireExplicitTenantEnvironmentAndPermission(string scenario, bool allowed)
    {
        var tenant = Guid.NewGuid();
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var consumer = new ExternalConsumerApp
        {
            AppID = "test-service",
            TenantId = tenant,
            Environment = "Production",
            Scopes = ["tasks.read"],
            IsEnabled = true,
            ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
        };
        var options = new ExternalConsumersOptions { Internal = [consumer] };
        var environment = "Production";
        var scope = "tasks.read";
        switch (scenario)
        {
            case "foreign-tenant": tenant = Guid.NewGuid(); break;
            case "foreign-environment": environment = "Development"; break;
            case "missing-scope": scope = "tasks.write"; break;
            case "disabled": consumer.IsEnabled = false; break;
            case "wrong-key": key = new string('x', 44); break;
            case "blank-key": key = string.Empty; break;
            case "malformed-hash": consumer.ApiKeySha256 = new string('z', 64); break;
        }
        Assert.Equal(allowed, options.FindAuthorizedConsumer(key, tenant, environment, scope) is not null);
    }

    [Fact]
    public async Task LocalSmsTransportNeverLogsRecipientOrOtp()
    {
        var logger = new Mock<ILogger<LoggingSmsSender>>();
        await new LoggingSmsSender(logger.Object).SendAsync("+966555123456", "Your OTP is 937412");
        var invocation = Assert.Single(logger.Invocations, x => x.Method.Name == "Log");
        var state = invocation.Arguments[2].ToString();
        Assert.DoesNotContain("937412", state, StringComparison.Ordinal);
        Assert.DoesNotContain("+966555123456", state, StringComparison.Ordinal);
        Assert.DoesNotContain("Your OTP", state, StringComparison.Ordinal);
    }
}
