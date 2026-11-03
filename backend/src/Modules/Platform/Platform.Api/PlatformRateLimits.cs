namespace Platform.Api;

/// <summary>Named rate-limit policies the control plane host defines; partitioned by caller IP.</summary>
public static class PlatformRateLimits
{
    /// <summary>Anonymous landing-page and signup calls.</summary>
    public const string Public = "platform-public";

    /// <summary>Bring-your-own database tests: each one opens a connection to a customer server.</summary>
    public const string DatabaseTest = "platform-database-test";

    /// <summary>Platform staff sign-in.</summary>
    public const string Login = "platform-login";
}
