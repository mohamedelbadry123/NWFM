using Auth.Domain.Constants;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

namespace Auth.Infrastructure.Services;

internal sealed class WorkflowRoleDirectory(AuthDbContext db, IOrgScopeProvider? scopes = null) : IWorkflowRoleDirectory
{
    public async Task<OrgScopeSet?> GetScopeForUserAsync(Guid userId, CancellationToken ct)
    {
        var roleIds = await GetRoleIdsForUserAsync(userId, ct);
        if (roleIds.Count == 0) return null;
        if (await db.Roles.AnyAsync(r => roleIds.Contains(r.Id) && (r.Name == Roles.Administrator || r.Name == Roles.Monitor), ct))
            return OrgScopeSet.Unrestricted();
        if (scopes is null) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        if (user.TeamId is Guid teamId) return await scopes.GetTeamScopeAsync(teamId, ct);
        var ownerId = OrgScopeOwnerTypes.UserOwnerId(userId);
        var rows = await db.OrgScopes.AsNoTracking().Where(s => s.OwnerType == OrgScopeOwnerTypes.User && s.OwnerId == ownerId && s.IsActive)
            .Select(s => new { s.Level, s.Code, Departments = s.Departments.Select(d => d.DepartmentCode).ToList(), Activities = s.ActivityTypes.Select(a => a.ActivityTypeCode).ToList() })
            .ToListAsync(ct);
        return OrgScopeSet.FromRows(rows.Select(r => new OrgScopeRow(r.Level, r.Code, r.Departments, r.Activities)), await scopes.GetHierarchyAsync(ct));
    }

    public async Task<IReadOnlyList<WorkflowLookupItem>> ListAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().Where(r => r.Name != null).OrderBy(r => r.Name)
            .Select(r => new WorkflowLookupItem(r.Id, r.Name!, r.Name!, r.Name!, null)).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await (from membership in db.UserRoles
                      join user in db.Users on membership.UserId equals user.Id
                      join role in db.Roles on membership.RoleId equals role.Id
                      where user.Id == userId && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
                          && (user.TeamId == null || db.Teams.Any(t => t.Id == user.TeamId && t.IsActive))
                      select role.Id).Distinct().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await (from membership in db.UserRoles
                      join user in db.Users on membership.UserId equals user.Id
                      join role in db.Roles on membership.RoleId equals role.Id
                      where roleIds.Contains(role.Id) && (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
                          && (user.TeamId == null || db.Teams.Any(t => t.Id == user.TeamId && t.IsActive))
                      select user.Id).Distinct().ToListAsync(ct);
    }
}
