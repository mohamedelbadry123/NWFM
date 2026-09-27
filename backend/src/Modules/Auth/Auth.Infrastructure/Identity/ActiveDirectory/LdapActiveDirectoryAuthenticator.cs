using System.DirectoryServices.Protocols;
using System.Net;
using Auth.Application.Common.Interfaces;
using Auth.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Identity.ActiveDirectory;

public sealed class LdapActiveDirectoryAuthenticator(
    IOptions<ActiveDirectorySettings> options,
    ILogger<LdapActiveDirectoryAuthenticator> logger) : IActiveDirectoryAuthenticator
{
    private readonly ActiveDirectorySettings _settings = options.Value;

    public bool IsEnabled => _settings.Enabled;

    public Task<ActiveDirectoryAuthResult> ValidateCredentialsAsync(
        string userName, string password, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return Task.FromResult(new ActiveDirectoryAuthResult(ActiveDirectoryAuthOutcome.Invalid,
                "Active Directory authentication is disabled."));

        try
        {
            string bindDn = FormatUserName(userName);
            int port = _settings.Port > 0
                ? _settings.Port
                : _settings.UseSsl
                    ? ActiveDirectorySettings.DefaultLdapsPort
                    : ActiveDirectorySettings.DefaultLdapPort;

            var identifier = new LdapDirectoryIdentifier(_settings.Server, port);
            var credential = new NetworkCredential(bindDn, password);

            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Basic,
                Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds)
            };

            connection.SessionOptions.ProtocolVersion = 3;

            if (_settings.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
            }
            else if (_settings.UseStartTls)
            {
                connection.SessionOptions.StartTransportLayerSecurity(null);
            }

            if (_settings.TrustServerCertificate)
            {
                connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            connection.Bind(credential);

            logger.LogInformation("LDAP bind succeeded for user {UserName} on {Server}:{Port}",
                userName, _settings.Server, port);

            return Task.FromResult(new ActiveDirectoryAuthResult(ActiveDirectoryAuthOutcome.Valid));
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            logger.LogWarning("LDAP bind failed (invalid credentials) for {UserName}: {Message}",
                userName, ex.Message);
            return Task.FromResult(new ActiveDirectoryAuthResult(ActiveDirectoryAuthOutcome.Invalid, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LDAP bind error for {UserName} on {Server}", userName, _settings.Server);
            return Task.FromResult(new ActiveDirectoryAuthResult(ActiveDirectoryAuthOutcome.Unavailable, ex.Message));
        }
    }

    private string FormatUserName(string userName) =>
        _settings.UserNameFormat switch
        {
            ActiveDirectoryUserNameFormat.UserPrincipalName =>
                userName.Contains('@') ? userName : $"{userName}@{_settings.UserPrincipalNameSuffix}",
            ActiveDirectoryUserNameFormat.DownLevel =>
                userName.Contains('\\') ? userName : $"{_settings.NetBiosName}\\{userName}",
            _ => userName
        };
}
