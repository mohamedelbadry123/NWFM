using Auth.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Identity;

public sealed class RoleLookup(RoleManager<ApplicationRole> roleManager) : IRoleLookup
{
    public async Task<string?> GetRoleIdByNameAsync(string roleName, CancellationToken ct = default)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        return role?.Id.ToString();
    }

    public async Task<bool> RoleExistsAsync(string roleName, CancellationToken ct = default) =>
        await roleManager.RoleExistsAsync(roleName);

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken ct = default) =>
        await roleManager.Roles
            .AsNoTracking()
            .Where(r => r.Name != null)
            .OrderBy(r => r.Name)
            .Select(r => new RoleSummary(r.Id.ToString(), r.Name!))
            .ToListAsync(ct);
}
