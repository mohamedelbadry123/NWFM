using Microsoft.AspNetCore.Http;

namespace Tenancy.Api.Middleware;

/// <summary>Paths the tenant resolution treats specially. Kept beside it so the two cannot drift.</summary>
public static class TenantRoutes
{
    public const string Refresh = "/api/v1/auth/refresh";

    public const string RefreshTokenField = "refreshToken";

    /// <summary>
    /// Never bound to a tenant: operational endpoints and API docs. The platform and public signup
    /// APIs are not here; they are served by NWFM.Platform.Api, never by this host.
    /// </summary>
    private static readonly string[] PlatformScopedPrefixes = ["/health", "/swagger", "/openapi"];

    public static bool IsPlatformScoped(PathString path) =>
        PlatformScopedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
