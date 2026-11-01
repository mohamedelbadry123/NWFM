using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Auth.Domain.Constants;
using Auth.Domain.Options;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.MultiTenancy;

namespace Auth.Infrastructure.Identity;

internal static class JwtTokenGenerator
{
    public static string GenerateAccessToken(
        ApplicationUser user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        JwtSettings settings,
        DateTime utcNow,
        Guid tenantId,
        long sessionId)
    {
        if (tenantId == Guid.Empty || sessionId <= 0 || string.IsNullOrWhiteSpace(user.SecurityStamp))
            throw new InvalidOperationException("Tokens require a tenant, persisted session and security stamp.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Email ?? user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaimTypes.TenantId, tenantId.ToString("D")),
            new(AppClaimTypes.SessionId, sessionId.ToString(CultureInfo.InvariantCulture)),
            new(AppClaimTypes.SecurityStamp, user.SecurityStamp)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        if (user.TeamId is Guid teamId)
        {
            claims.Add(new Claim(AppClaimTypes.TeamId, teamId.ToString()));
            if (!string.IsNullOrWhiteSpace(user.UserName))
                claims.Add(new Claim(AppClaimTypes.UserCode, user.UserName));
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaimTypes.Permission, p)));

        return Write(claims, settings, utcNow, utcNow.AddMinutes(settings.ExpiryMinutes));
    }

    /// <summary>
    /// A token for platform support acting as <paramref name="user"/> under a consented session. It has
    /// no refresh-token session: every request re-checks the catalog grant instead, and it expires with
    /// the session.
    /// </summary>
    public static string GenerateSupportAccessToken(
        ApplicationUser user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        JwtSettings settings,
        DateTime utcNow,
        Guid tenantId,
        SupportSessionClaims support,
        DateTime expiresAtUtc)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(user.SecurityStamp))
            throw new InvalidOperationException("Support tokens require a tenant and security stamp.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Email ?? user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaimTypes.TenantId, tenantId.ToString("D")),
            new(AppClaimTypes.SecurityStamp, user.SecurityStamp),
            new(TenantClaimTypes.Actor, support.PlatformUserId.ToString("D")),
            new(TenantClaimTypes.ActorName, support.PlatformUserName),
            new(TenantClaimTypes.SupportScope, support.Scope),
            new(TenantClaimTypes.SupportSessionId, support.SessionId.ToString("D"))
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaimTypes.Permission, p)));

        return Write(claims, settings, utcNow, expiresAtUtc);
    }

    private static string Write(IEnumerable<Claim> claims, JwtSettings settings, DateTime utcNow, DateTime expiresAtUtc)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: utcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal sealed record SupportSessionClaims(Guid SessionId, Guid PlatformUserId, string PlatformUserName, string Scope);
