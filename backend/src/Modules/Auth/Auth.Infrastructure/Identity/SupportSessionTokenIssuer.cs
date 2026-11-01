using Auth.Application.Common.Interfaces;
using Auth.Domain.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Results;
using AppErrors = Auth.Application.Constants.AuthErrors;

namespace Auth.Infrastructure.Identity;

/// <summary>
/// Mints the tenant token for a support session whose handoff code was just redeemed. Runs in the
/// request's own tenant scope (the resolution middleware has already bound the tenant), so the
/// tenant signing key never leaves the tenant API.
/// </summary>
internal sealed class SupportSessionTokenIssuer(
    UserManager<ApplicationUser> userManager,
    IPermissionResolver permissionResolver,
    ITenantContext tenantContext,
    IOptions<JwtSettings> jwtOptions,
    TimeProvider timeProvider)
    : ISupportSessionTokenIssuer
{
    public async Task<Result<SupportSessionTokenDto>> IssueAsync(SupportSessionTicket session, CancellationToken cancellationToken)
    {
        var settings = jwtOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningKey))
            return Result<SupportSessionTokenDto>.Failure(AppErrors.SigningNotConfigured);

        var tenant = tenantContext.Current;
        if (tenant is null || tenant.Id != session.TenantId)
            return Result<SupportSessionTokenDto>.Failure(AppErrors.SupportSessionWrongTenant);

        var user = await userManager.FindByIdAsync(session.ImpersonatedUserId);
        if (user is null)
            return Result<SupportSessionTokenDto>.Failure(AppErrors.NotFound);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // Read back from SQL Server the expiry is DateTimeKind.Unspecified; the token handler would
        // take it as local time and, east of UTC, mint a token that is already expired.
        var expiresAtUtc = DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc);
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var permissions = await permissionResolver.GetPermissionCodesForUserAsync(user.Id.ToString(), cancellationToken);
        var accessToken = JwtTokenGenerator.GenerateSupportAccessToken(
            user, roles, permissions, settings, utcNow, tenant.Id,
            new SupportSessionClaims(session.SessionId, session.PlatformUserId, session.PlatformUserName, session.Scope),
            expiresAtUtc);

        return Result<SupportSessionTokenDto>.Success(new SupportSessionTokenDto
        {
            AccessToken = accessToken,
            ExpiresInSeconds = Math.Max(1, (int)(expiresAtUtc - utcNow).TotalSeconds),
            SessionId = session.SessionId,
            TenantId = tenant.Id,
            TenantKey = tenant.Key,
            SupportScope = session.Scope,
            ImpersonatedUserId = user.Id.ToString(),
            ImpersonatedUserName = user.UserName ?? user.Email ?? user.Id.ToString(),
            ActorName = session.PlatformUserName,
            Roles = roles,
            Permissions = permissions
        });
    }
}
