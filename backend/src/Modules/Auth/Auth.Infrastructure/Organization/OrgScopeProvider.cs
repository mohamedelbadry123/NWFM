using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Options;
using NWFM.Shared.Organization;

namespace Auth.Infrastructure.Organization;

/// <summary>
/// Auth's answer to "whose territory is whose", for the modules that must narrow work to it. The org
/// hierarchy is reference data and is cached; scope rows are read per call — one indexed read — so a
/// change on the teams or users screen applies to the very next request.
/// </summary>
internal sealed class OrgScopeProvider(
    IAuthDbContext db,
    ICurrentUser user,
    ICacheService cache,
    IOptions<CacheSettings> cacheSettings) : IOrgScopeProvider, IOrgDirectory
{
    public async Task<OrgScopeSet> GetCurrentUserScopeAsync(CancellationToken cancellationToken)
    {
        if (user.IsInRole(Roles.Administrator) || user.IsInRole(Roles.Monitor))
        {
            return OrgScopeSet.Unrestricted();
        }

        // A team login inherits the crew's territory — the scope rows live on the team, not the account.
        if (user.TeamId is Guid teamId)
        {
            return await GetTeamScopeAsync(teamId, cancellationToken);
        }

        // An unauthenticated caller reaches nothing here — every endpoint that asks is authorised —
        // but an anonymous background caller is treated as the system, not as a user with no rows.
        if (!Guid.TryParse(user.Id, out var userId))
        {
            return OrgScopeSet.Unrestricted();
        }

        return await ScopeOfAsync(OrgScopeOwnerTypes.User, OrgScopeOwnerTypes.UserOwnerId(userId), cancellationToken);
    }

    public Task<OrgScopeSet> GetTeamScopeAsync(Guid teamId, CancellationToken cancellationToken) =>
        ScopeOfAsync(OrgScopeOwnerTypes.Team, OrgScopeOwnerTypes.TeamOwnerId(teamId), cancellationToken);

    public ValueTask<OrgHierarchy> GetHierarchyValueAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            CacheKeys.Lookups.OrgHierarchy,
            LoadHierarchyAsync,
            cacheSettings.Value.ToLookupEntryOptions(),
            cancellationToken);

    public async Task<OrgHierarchy> GetHierarchyAsync(CancellationToken cancellationToken) =>
        await GetHierarchyValueAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, OrgTeamInfo>> GetTeamsAsync(
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return new Dictionary<Guid, OrgTeamInfo>();
        }

        var ids = teamIds.Distinct().ToList();

        return await db.Teams
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new OrgTeamInfo(t.Id, t.Name, t.Mobile, t.IsActive))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<OrgTeamCoverage>> GetActiveTeamsAsync(CancellationToken cancellationToken)
    {
        var teams = await db.Teams
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new OrgTeamInfo(t.Id, t.Name, t.Mobile, t.IsActive))
            .ToListAsync(cancellationToken);

        if (teams.Count == 0)
        {
            return [];
        }

        // Every active team's rows in one read, rather than one read per team.
        var rows = await db.OrgScopes
            .AsNoTracking()
            .Where(s => s.OwnerType == OrgScopeOwnerTypes.Team && s.IsActive)
            .Select(s => new { s.OwnerId, s.Level, s.Code, s.DepartmentId })
            .ToListAsync(cancellationToken);

        var byOwner = rows.ToLookup(r => r.OwnerId, StringComparer.OrdinalIgnoreCase);
        var hierarchy = await GetHierarchyValueAsync(cancellationToken);

        var coverage = new List<OrgTeamCoverage>(teams.Count);

        foreach (var team in teams)
        {
            var teamRows = byOwner[OrgScopeOwnerTypes.TeamOwnerId(team.Id)].ToList();

            // A team with no rows is left out rather than read as unrestricted: a crew with no
            // territory cannot even sign in, so it is not ready to be handed work.
            if (teamRows.Count == 0)
            {
                continue;
            }

            coverage.Add(new OrgTeamCoverage(
                team,
                OrgScopeSet.FromRows(teamRows.Select(r => new OrgScopeRow(r.Level, r.Code, r.DepartmentId)), hierarchy)));
        }

        return coverage;
    }

    public async Task<IReadOnlyDictionary<string, OrgUnitName>> GetUnitNamesAsync(
        string level,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken)
    {
        var wanted = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0)
        {
            return new Dictionary<string, OrgUnitName>(StringComparer.OrdinalIgnoreCase);
        }

        // SQL Server compares these codes case-insensitively, so the IN list matches however the
        // work stamped them.
        IQueryable<OrgUnitName>? names = level switch
        {
            OrgUnitLevels.Department => db.Departments.AsNoTracking()
                .Where(x => wanted.Contains(x.Code)).Select(x => new OrgUnitName(x.Code, x.NameEn, x.NameAr)),
            OrgLevels.Cluster => db.Clusters.AsNoTracking()
                .Where(x => wanted.Contains(x.Code)).Select(x => new OrgUnitName(x.Code, x.NameEn, x.NameAr)),
            OrgLevels.Cbu => db.Cbus.AsNoTracking()
                .Where(x => wanted.Contains(x.Code)).Select(x => new OrgUnitName(x.Code, x.NameEn, x.NameAr)),
            OrgLevels.Branch => db.Branches.AsNoTracking()
                .Where(x => wanted.Contains(x.Code)).Select(x => new OrgUnitName(x.Code, x.NameEn, x.NameAr)),
            OrgLevels.OperationArea => db.OperationAreas.AsNoTracking()
                .Where(x => wanted.Contains(x.Code)).Select(x => new OrgUnitName(x.Code, x.NameEn, x.NameAr)),
            _ => null,
        };

        if (names is null)
        {
            return new Dictionary<string, OrgUnitName>(StringComparer.OrdinalIgnoreCase);
        }

        var found = await names.ToListAsync(cancellationToken);

        var byCode = new Dictionary<string, OrgUnitName>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in found)
        {
            byCode.TryAdd(name.Code, name);
        }

        return byCode;
    }

    private async Task<OrgScopeSet> ScopeOfAsync(string ownerType, string ownerId, CancellationToken cancellationToken)
    {
        var rows = await db.OrgScopes
            .AsNoTracking()
            .Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId && s.IsActive)
            .Select(s => new OrgScopeRow(s.Level, s.Code, s.DepartmentId))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return OrgScopeSet.Unrestricted();
        }

        var hierarchy = await GetHierarchyValueAsync(cancellationToken);
        return OrgScopeSet.FromRows(rows, hierarchy);
    }

    private async ValueTask<OrgHierarchy> LoadHierarchyAsync(CancellationToken cancellationToken)
    {
        var cbus = await db.Cbus.AsNoTracking()
            .Select(x => new { x.Code, x.ClusterCode })
            .ToListAsync(cancellationToken);

        var branches = await db.Branches.AsNoTracking()
            .Select(x => new { x.Code, x.CbuCode })
            .ToListAsync(cancellationToken);

        var areas = await db.OperationAreas.AsNoTracking()
            .Select(x => new { x.Code, x.CbuCode })
            .ToListAsync(cancellationToken);

        return OrgHierarchy.Build(
            cbus.Select(x => (x.Code, (string?)x.ClusterCode)),
            branches.Select(x => (x.Code, x.CbuCode)),
            areas.Select(x => (x.Code, (string?)x.CbuCode)));
    }
}
