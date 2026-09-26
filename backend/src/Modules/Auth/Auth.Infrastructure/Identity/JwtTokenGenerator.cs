using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Auth.Domain.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Identity;

internal static class JwtTokenGenerator
{
    public static string GenerateAccessToken(
        ApplicationUser user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        JwtSettings settings,
        DateTime utcNow)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Email ?? user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: utcNow,
            expires: utcNow.AddMinutes(settings.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
