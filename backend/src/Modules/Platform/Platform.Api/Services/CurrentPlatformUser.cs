using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NWFM.Shared.MultiTenancy;
using Platform.Application.Common.Security;

namespace Platform.Api.Services;

/// <summary>
/// Reads only an identity issued by the platform scheme, so a tenant token can never be mistaken
/// for platform staff even where both schemes ran.
/// </summary>
public sealed class CurrentPlatformUser(IHttpContextAccessor httpContextAccessor) : IPlatformUser
{
    private ClaimsIdentity? Identity =>
        httpContextAccessor.HttpContext?.User.Identities.FirstOrDefault(i =>
            i.IsAuthenticated && i.AuthenticationType == PlatformAuthentication.Scheme);

    public Guid? Id => Guid.TryParse(Identity?.FindFirst(PlatformAuthentication.SubjectClaim)?.Value, out var id) ? id : null;

    public string? Email => Identity?.FindFirst(PlatformAuthentication.EmailClaim)?.Value;

    public string? DisplayName => Identity?.FindFirst(PlatformAuthentication.DisplayNameClaim)?.Value;

    public string? Role => Identity?.FindFirst(PlatformAuthentication.RoleClaim)?.Value;

    public bool IsAuthenticated => Identity is not null;
}
