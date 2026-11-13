using System.Globalization;
using System.Security.Claims;
using Auth.Domain.Constants;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Options;

namespace Auth.Infrastructure.Identity;

/// <summary>
/// Cryptographic token validation establishes the issuer, not current access.
/// Recheck the dedicated tenant database and rebuild grants on every request.
/// </summary>
public sealed class TenantSessionValidator(
    AuthDbContext db,
    ICurrentTenant tenant,
    IOptionsMonitor<TenantIsolationOptions> isolationOptions,
    TimeProvider timeProvider)
{
    public async Task<ClaimsPrincipal?> ValidateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!isolationOptions.CurrentValue.IsActive ||
            principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(SingleClaim(principal, AppClaimTypes.TenantId), out var tenantId) ||
            tenantId == Guid.Empty || tenantId != tenant.OrganizationId ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !long.TryParse(SingleClaim(principal, AppClaimTypes.SessionId), NumberStyles.None,
                CultureInfo.InvariantCulture, out var sessionId) || sessionId <= 0)
            return null;

        var stamp = SingleClaim(principal, AppClaimTypes.SecurityStamp);
        var now = timeProvider.GetUtcNow();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || string.IsNullOrWhiteSpace(stamp) ||
            !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal) ||
            (user.LockoutEnabled && user.LockoutEnd > now))
            return null;

        if (!await db.UserRefreshTokens.AsNoTracking().AnyAsync(x =>
                x.Id == sessionId && x.UserId == userId.ToString() &&
                x.RevokedAtUtc == null && x.ExpiresAtUtc > now.UtcDateTime, ct))
            return null;

        var rights = await CurrentActorAccessRules.ReadAsync(db, user, now, ct);
        if (rights is null) return null;
        var roles = rights.Roles;

        var roleIds = roles.Select(x => x.Id.ToString()).ToArray();
        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId) && x.Permission.IsActive)
            .Select(x => x.Permission.Code).Distinct().ToListAsync(ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.UserName ?? user.Id.ToString("D")),
            new(AppClaimTypes.TenantId, tenantId.ToString("D")),
            new(AppClaimTypes.SessionId, sessionId.ToString(CultureInfo.InvariantCulture)),
            new(AppClaimTypes.SecurityStamp, stamp)
        };
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        if (user.TeamId is Guid currentTeam)
        {
            claims.Add(new Claim(AppClaimTypes.TeamId, currentTeam.ToString("D")));
            claims.Add(new Claim(AppClaimTypes.UserCode, user.UserName ?? string.Empty));
        }
        claims.AddRange(roles.Where(x => x.Name is not null).Select(x => new Claim(ClaimTypes.Role, x.Name!)));
        claims.AddRange(permissions.Select(x => new Claim(PermissionClaimTypes.Permission, x)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity.AuthenticationType,
            ClaimTypes.Name, ClaimTypes.Role));
    }

    private static string? SingleClaim(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }
}
