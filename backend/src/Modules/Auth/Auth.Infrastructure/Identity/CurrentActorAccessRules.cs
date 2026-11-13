using Auth.Domain.Constants;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Organization;

namespace Auth.Infrastructure.Identity;

/// <summary>Current identity rights shared by HTTP sessions and durable actor execution.
/// Session credentials/liveness are intentionally checked only by TenantSessionValidator.</summary>
internal static class CurrentActorAccessRules
{
    internal sealed record Role(Guid Id, string? Name);
    internal sealed record Rights(IReadOnlyList<Role> Roles, IReadOnlyList<OrgScopeRow> ScopeRows)
    {
        public bool IsAdministrator => Roles.Any(r => r.Name == Auth.Domain.Constants.Roles.Administrator);
    }

    internal static async Task<Rights?> ReadAsync(AuthDbContext db, ApplicationUser user, DateTimeOffset now, CancellationToken ct)
    {
        if (user.LockoutEnabled && user.LockoutEnd > now) return null;
        var roles = await (from membership in db.UserRoles.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                           where membership.UserId == user.Id
                           select new Role(role.Id, role.Name)).ToListAsync(ct);
        // Administrator/Monitor never bypass team membership eligibility.
        if (user.TeamId is Guid team && (!roles.Any(r => r.Name == Roles.FieldTeam)
            || !await db.Teams.AsNoTracking().AnyAsync(t => t.Id == team && t.IsActive, ct))) return null;
        if (roles.Any(r => r.Name == Roles.Administrator)) return new(roles, []);
        var ownerType = user.TeamId is null ? OrgScopeOwnerTypes.User : OrgScopeOwnerTypes.Team;
        var ownerId = (user.TeamId ?? user.Id).ToString("D");
        var rows = await db.OrgScopes.AsNoTracking().Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId && s.IsActive)
            .Select(s => new
            {
                s.Level,
                s.Code,
                Departments = s.Departments.Select(d => d.DepartmentCode).ToList(),
                Activities = s.ActivityTypes.Select(a => a.ActivityTypeCode).ToList()
            }).ToListAsync(ct);
        // Materialize the same rows that establish eligibility. No later no-rows => unrestricted lookup.
        return rows.Count == 0 ? null : new(roles, rows.Select(r => new OrgScopeRow(r.Level, r.Code, r.Departments, r.Activities)).ToList());
    }
}
