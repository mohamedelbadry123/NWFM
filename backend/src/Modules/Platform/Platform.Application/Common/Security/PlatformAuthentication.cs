namespace Platform.Application.Common.Security;

/// <summary>The platform staff bearer scheme and the claims its tokens carry.</summary>
public static class PlatformAuthentication
{
    /// <summary>JwtBearer scheme for platform staff tokens; also the identity's authentication type.</summary>
    public const string Scheme = "Platform";

    public const string RoleClaim = "platform_role";

    public const string DisplayNameClaim = "platform_name";

    /// <summary>Registered JWT claims, read as issued: the platform scheme does not remap inbound claims.</summary>
    public const string SubjectClaim = "sub";

    public const string EmailClaim = "email";
}
