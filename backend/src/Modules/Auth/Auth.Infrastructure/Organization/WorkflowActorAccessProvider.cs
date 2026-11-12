using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;

namespace Auth.Infrastructure.Organization;

internal sealed class WorkflowActorAccessProvider(AuthDbContext db,
    IPermissionResolver permissions, OrgScopeProvider scopes, TimeProvider timeProvider) : IWorkflowActorAccess
{
    public async Task<WorkflowActorAccess?> GetAsync(Guid actorId, CancellationToken ct)
    {
        if (actorId == Guid.Empty) return null;
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId, ct);
        if (actor is null) return null;
        var rights = await CurrentActorAccessRules.ReadAsync(db, actor, timeProvider.GetUtcNow(), ct);
        if (rights is null) return null;
        var hierarchy = await scopes.GetHierarchyAsync(ct);
        var scope = rights.IsAdministrator || rights.Roles.Any(r => r.Name == Roles.Monitor)
            ? OrgScopeSet.Unrestricted()
            : OrgScopeSet.FromRows(rights.ScopeRows, hierarchy);
        return new(await permissions.GetPermissionCodesForUserAsync(actor.Id.ToString(), ct), scope,
            hierarchy, rights.IsAdministrator);
    }
}
