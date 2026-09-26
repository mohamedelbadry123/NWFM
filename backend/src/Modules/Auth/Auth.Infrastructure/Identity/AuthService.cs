using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Options;
using Auth.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Results;

namespace Auth.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AuthDbContext context,
    IPermissionResolver permissionResolver,
    IActiveDirectoryAuthenticator activeDirectory,
    ICacheService cache,
    IOptions<JwtSettings> jwtOptions,
    IOptions<SsoSettings> ssoOptions,
    TimeProvider timeProvider) : IAuthService
{
    private static readonly Error InvalidCredentials = new("Auth.InvalidCredentials", "Invalid username or password.");
    private static readonly Error LockedOut = new("Auth.LockedOut", "User account is locked out or inactive.");
    private static readonly Error SsoRequired = new("Auth.SsoRequired", "Sign in through SSO.");
    private static readonly Error TeamInactive = new("Auth.TeamInactive", "The team linked to this login is no longer active.");
    private static readonly Error TeamScopeRequired = new("Auth.TeamScopeRequired", "This team has no territory scope assigned.");
    private static readonly Error UserScopeRequired = new("Auth.UserScopeRequired", "This account has no territory scope assigned.");
    private static readonly Error InvalidRefreshToken = new("Auth.InvalidRefreshToken", "Refresh token is invalid or expired.");
    private static readonly Error JwtNotConfigured = new("Auth.JwtNotConfigured", "JWT signing is not configured.");
    private static readonly Error DirectoryUnavailable = new("Auth.DirectoryUnavailable", "Sign-in is temporarily unavailable.");
    private static readonly Error InvalidSsoCode = new("Auth.InvalidSsoCode", "Sign-in code is invalid or expired.");
    private static readonly Error InvalidTeamCredentials = new("Auth.InvalidTeamCredentials", "Invalid team code or password.");

    public async Task<Result<AuthTokenDto>> LoginAsync(string userName, string password, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByNameAsync(userName);
        if (user is null || !await userManager.CheckPasswordAsync(user, password))
            return Result<AuthTokenDto>.Failure(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            return Result<AuthTokenDto>.Failure(LockedOut);

        Result<AuthTokenDto>? ssoGate = await EnsureLocalLoginAllowedAsync(user);
        if (ssoGate is not null) return ssoGate;

        if (user.TeamId is Guid teamId && !await TeamIsActiveAsync(teamId, ct))
            return Result<AuthTokenDto>.Failure(TeamInactive);

        Result<AuthTokenDto>? scopeGate = await EnsureRequiredScopeAsync(user, ct);
        if (scopeGate is not null) return scopeGate;

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result<TeamLoginContext>> AuthenticateTeamAsync(string userCode, string password, CancellationToken ct)
    {
        string trimmed = userCode.Trim();
        ApplicationUser? user = await userManager.FindByNameAsync(trimmed);
        if (user is null)
            return Result<TeamLoginContext>.Failure(InvalidTeamCredentials);

        Result<AuthTokenDto>? pwGate = await VerifyTeamPasswordAsync(user, trimmed, password, ct);
        if (pwGate is not null)
            return Result<TeamLoginContext>.Failure(pwGate.Error);

        Result<AuthTokenDto>? postGate = await RunTeamPostPasswordGatesAsync(user, ct);
        if (postGate is not null)
            return Result<TeamLoginContext>.Failure(postGate.Error);

        Guid teamId = user.TeamId!.Value;
        string? mobile = await context.Teams.AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => t.Mobile)
            .FirstOrDefaultAsync(ct);

        return Result<TeamLoginContext>.Success(new TeamLoginContext
        {
            UserId = user.Id.ToString(),
            UserName = user.UserName ?? trimmed,
            TeamId = teamId,
            Mobile = mobile
        });
    }

    public async Task<Result<AuthTokenDto>> IssueTeamTokensAsync(string userId, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Result<AuthTokenDto>.Failure(InvalidTeamCredentials);

        Result<AuthTokenDto>? postGate = await RunTeamPostPasswordGatesAsync(user, ct);
        if (postGate is not null) return postGate;

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result<AuthTokenDto>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        JwtSettings settings = jwtOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningKey))
            return Result<AuthTokenDto>.Failure(JwtNotConfigured);

        string tokenHash = HashToken(refreshToken);
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        UserRefreshToken? stored = await context.UserRefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

        if (stored is null || !stored.IsActive(utcNow))
            return Result<AuthTokenDto>.Failure(InvalidRefreshToken);

        ApplicationUser? user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null || await userManager.IsLockedOutAsync(user))
            return Result<AuthTokenDto>.Failure(InvalidRefreshToken);

        Result<AuthTokenDto>? scopeGate = await EnsureRequiredScopeAsync(user, ct);
        if (scopeGate is not null) return scopeGate;

        stored.Revoke(utcNow);
        await context.SaveChangesAsync(ct);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result<bool>> LogoutAsync(string userId, string? refreshToken, CancellationToken ct)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        IQueryable<UserRefreshToken> sessions = context.UserRefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > utcNow);

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            string tokenHash = HashToken(refreshToken);
            sessions = sessions.Where(x => x.TokenHash == tokenHash);
        }

        List<UserRefreshToken> active = await sessions.ToListAsync(ct);
        foreach (UserRefreshToken s in active) s.Revoke(utcNow);
        if (active.Count > 0) await context.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<SsoSignInResultDto>> SignInWithSamlAsync(string nameId, string? sessionIndex, CancellationToken ct)
    {
        ApplicationUser? user = await userManager.FindByNameAsync(nameId.Trim());
        if (user is null) return Denied(SsoDefaults.DenialReasons.NotProvisioned);
        if (user.TeamId is not null || await userManager.IsInRoleAsync(user, Roles.FieldTeam))
            return Denied(SsoDefaults.DenialReasons.CrewAccount);
        if (await userManager.IsLockedOutAsync(user))
            return Denied(SsoDefaults.DenialReasons.Inactive);
        if (!await userManager.IsInRoleAsync(user, Roles.Administrator) &&
            !await HasActiveScopeAsync(OrgScopeOwnerTypes.User, user.Id.ToString(), ct))
            return Denied(SsoDefaults.DenialReasons.NoScope);

        SsoSettings settings = ssoOptions.Value;
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        string codePlainText = GenerateRefreshToken();

        await context.SsoAuthorizationCodes
            .Where(x => x.UserId == user.Id.ToString() && (x.ConsumedAtUtc != null || x.ExpiresAtUtc <= utcNow))
            .ExecuteDeleteAsync(ct);

        context.SsoAuthorizationCodes.Add(SsoAuthorizationCode.Create(
            user.Id.ToString(), HashToken(codePlainText), sessionIndex,
            utcNow.AddSeconds(settings.AuthorizationCodeLifetimeSeconds), utcNow));
        await context.SaveChangesAsync(ct);

        return Result<SsoSignInResultDto>.Success(SsoSignInResultDto.Granted(codePlainText));

        static Result<SsoSignInResultDto> Denied(string reason) =>
            Result<SsoSignInResultDto>.Success(SsoSignInResultDto.Denied(reason));
    }

    public async Task<Result<AuthTokenDto>> ExchangeSsoCodeAsync(string code, CancellationToken ct)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        string codeHash = HashToken(code);
        SsoAuthorizationCode? stored = await context.SsoAuthorizationCodes.FirstOrDefaultAsync(x => x.CodeHash == codeHash, ct);
        if (stored is null || !stored.IsActive(utcNow))
            return Result<AuthTokenDto>.Failure(InvalidSsoCode);

        stored.Consume(utcNow);
        await context.SaveChangesAsync(ct);

        ApplicationUser? user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null || await userManager.IsLockedOutAsync(user))
            return Result<AuthTokenDto>.Failure(InvalidSsoCode);

        Result<AuthTokenDto>? scopeGate = await EnsureRequiredScopeAsync(user, ct);
        if (scopeGate is not null) return scopeGate;

        return await IssueTokensAsync(user, ct);
    }

    private async Task<Result<AuthTokenDto>> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        JwtSettings settings = jwtOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.SigningKey))
            return Result<AuthTokenDto>.Failure(JwtNotConfigured);

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        IList<string> roles = await userManager.GetRolesAsync(user);
        IReadOnlyList<string> permissions = await permissionResolver.GetPermissionCodesForUserAsync(user.Id.ToString(), ct);
        string accessToken = JwtTokenGenerator.GenerateAccessToken(user, roles.ToList(), permissions, settings, utcNow);
        string refreshTokenPlainText = GenerateRefreshToken();
        DateTime refreshExpiresAt = utcNow.AddDays(settings.RefreshTokenExpiryDays);

        context.UserRefreshTokens.Add(
            UserRefreshToken.Create(user.Id.ToString(), HashToken(refreshTokenPlainText), refreshExpiresAt, utcNow));
        await context.SaveChangesAsync(ct);

        return Result<AuthTokenDto>.Success(new AuthTokenDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenPlainText,
            ExpiresInSeconds = settings.ExpiryMinutes * 60,
            UserName = user.UserName ?? string.Empty,
            Roles = roles.ToList(),
            Permissions = permissions.ToList()
        });
    }

    private async Task<Result<AuthTokenDto>?> EnsureLocalLoginAllowedAsync(ApplicationUser user)
    {
        SsoSettings sso = ssoOptions.Value;
        if (!sso.Enabled || user.TeamId is not null) return null;
        if (sso.AllowLocalLoginForAdministrators && await userManager.IsInRoleAsync(user, Roles.Administrator))
            return null;
        return Result<AuthTokenDto>.Failure(SsoRequired);
    }

    private async Task<Result<AuthTokenDto>?> RunTeamPostPasswordGatesAsync(ApplicationUser user, CancellationToken ct)
    {
        if (await userManager.IsLockedOutAsync(user))
            return Result<AuthTokenDto>.Failure(TeamInactive);
        if (user.TeamId is not Guid teamId || !await userManager.IsInRoleAsync(user, Roles.FieldTeam))
            return Result<AuthTokenDto>.Failure(InvalidTeamCredentials);
        if (!await TeamIsActiveAsync(teamId, ct))
            return Result<AuthTokenDto>.Failure(TeamInactive);
        return await EnsureRequiredScopeAsync(user, ct);
    }

    private async Task<Result<AuthTokenDto>?> VerifyTeamPasswordAsync(
        ApplicationUser user, string userCode, string password, CancellationToken ct)
    {
        if (!activeDirectory.IsEnabled)
        {
            return await userManager.CheckPasswordAsync(user, password)
                ? null
                : Result<AuthTokenDto>.Failure(InvalidTeamCredentials);
        }

        ActiveDirectoryAuthResult result = await activeDirectory.ValidateCredentialsAsync(userCode, password, ct);
        return result.Outcome switch
        {
            ActiveDirectoryAuthOutcome.Valid => null,
            ActiveDirectoryAuthOutcome.Unavailable => Result<AuthTokenDto>.Failure(DirectoryUnavailable),
            _ => Result<AuthTokenDto>.Failure(InvalidTeamCredentials)
        };
    }

    private async Task<Result<AuthTokenDto>?> EnsureRequiredScopeAsync(ApplicationUser user, CancellationToken ct)
    {
        if (await userManager.IsInRoleAsync(user, Roles.Administrator)) return null;
        if (user.TeamId is Guid teamId)
        {
            return await HasActiveScopeAsync(OrgScopeOwnerTypes.Team, OrgScopeOwnerTypes.TeamOwnerId(teamId), ct)
                ? null : Result<AuthTokenDto>.Failure(TeamScopeRequired);
        }
        return await HasActiveScopeAsync(OrgScopeOwnerTypes.User, user.Id.ToString(), ct)
            ? null : Result<AuthTokenDto>.Failure(UserScopeRequired);
    }

    private Task<bool> TeamIsActiveAsync(Guid teamId, CancellationToken ct) =>
        context.Teams.AsNoTracking()
            .AnyAsync(t => t.Id == teamId && t.IsActive, ct);

    private Task<bool> HasActiveScopeAsync(string ownerType, string ownerId, CancellationToken ct) =>
        context.OrgScopes.AsNoTracking()
            .AnyAsync(x => x.OwnerType == ownerType && x.OwnerId == ownerId && x.IsActive, ct);

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
