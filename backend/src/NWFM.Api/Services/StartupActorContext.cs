using System.Security.Claims;
using Auth.Domain.Constants;
using Auth.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace NWFM.Api.Services;

/// <summary>Server-owned identity for the opt-in startup demo initializer. Never available to HTTP requests.</summary>
public sealed class StartupActorContext(IHttpContextAccessor http, UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principals)
{
    private readonly AsyncLocal<ClaimsPrincipal?> _current = new();
    public ClaimsPrincipal? Principal => http.HttpContext is null ? _current.Value : null;

    public async Task RunWorkspaceDemoAsync(Func<Guid, Task> initialize)
    {
        if (http.HttpContext is not null || _current.Value is not null)
            throw new UnauthorizedAccessException("Startup identity is restricted to isolated server initialization.");
        var admin = await users.FindByNameAsync("administrator@localhost");
        if (admin is null || admin.Id == Guid.Empty || await users.IsLockedOutAsync(admin)
            || !await users.IsInRoleAsync(admin, Roles.Administrator))
            throw new UnauthorizedAccessException("Workspace demo initialization requires the enabled local administrator account.");
        var principal = await principals.CreateAsync(admin);
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirstValue(ClaimTypes.NameIdentifier) != admin.Id.ToString()
            || !principal.IsInRole(Roles.Administrator))
            throw new UnauthorizedAccessException("Startup principal does not identify the authorized administrator.");
        _current.Value = principal;
        try { await initialize(admin.Id); }
        finally { _current.Value = null; }
    }
}
