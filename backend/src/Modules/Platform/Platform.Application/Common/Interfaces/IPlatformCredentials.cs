
namespace Platform.Application.Common.Interfaces;

/// <summary>Password hashing and token issuing for platform staff, kept out of tenant Identity.</summary>
public interface IPlatformCredentials
{
    string HashPassword(PlatformUser user, string password);

    PlatformPasswordCheck VerifyPassword(PlatformUser user, string password);

    /// <summary>Null when <c>PlatformJwt:SigningKey</c> is not configured.</summary>
    PlatformAccessToken? IssueToken(PlatformUser user);
}

public enum PlatformPasswordCheck
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}

public sealed record PlatformAccessToken(string AccessToken, int ExpiresInSeconds);
