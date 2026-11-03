using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;

namespace Platform.Infrastructure.Security;

internal sealed class PlatformCredentials(
    IPasswordHasher<PlatformUser> passwordHasher,
    IOptions<PlatformJwtSettings> jwtOptions,
    TimeProvider timeProvider)
    : IPlatformCredentials
{
    public string HashPassword(PlatformUser user, string password) => passwordHasher.HashPassword(user, password);

    public PlatformPasswordCheck VerifyPassword(PlatformUser user, string password) =>
        passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => PlatformPasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PlatformPasswordCheck.SuccessRehashNeeded,
            _ => PlatformPasswordCheck.Failed
        };

    public PlatformAccessToken? IssueToken(PlatformUser user)
    {
        var settings = jwtOptions.Value;

        if (string.IsNullOrWhiteSpace(settings.SigningKey))
        {
            return null;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = utcNow,
            NotBefore = utcNow,
            Expires = utcNow.AddMinutes(settings.ExpiryMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(PlatformAuthentication.DisplayNameClaim, user.DisplayName),
                new Claim(PlatformAuthentication.RoleClaim, user.Role)
            ])
        };

        return new PlatformAccessToken(new JsonWebTokenHandler().CreateToken(descriptor), settings.ExpiryMinutes * 60);
    }
}
