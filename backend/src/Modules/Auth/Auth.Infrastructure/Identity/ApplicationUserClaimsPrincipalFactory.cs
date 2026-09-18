using System.Security.Claims;
using Auth.Application.Common.Interfaces;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Identity;

public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IPermissionResolver permissionResolver,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);
        IReadOnlyList<string> permissions = await permissionResolver.GetPermissionCodesForUserAsync(user.Id.ToString());
        foreach (string permission in permissions)
            identity.AddClaim(new Claim(PermissionClaimTypes.Permission, permission));
        return identity;
    }
}
