using NWFM.Shared.Organization;

namespace NWFM.Shared.Integration.Organization;

/// <summary>
/// Whose territory is whose. Implemented by Auth, which owns the scope rows and the org hierarchy;
/// read by any module that must narrow work to the caller's territory without referencing Auth.
/// </summary>
public interface IOrgScopeProvider
{
    /// <summary>
    /// The current caller's coverage: unrestricted for an Administrator or a Monitor; a team login
    /// inherits its team's rows (the crew's territory lives on the team, not the account); anyone
    /// else has their own rows. No rows at all means unrestricted.
    /// </summary>
    Task<OrgScopeSet> GetCurrentUserScopeAsync(CancellationToken cancellationToken);

    /// <summary>A team's coverage.</summary>
    Task<OrgScopeSet> GetTeamScopeAsync(Guid teamId, CancellationToken cancellationToken);

    /// <summary>The org hierarchy, for expanding a cluster filter into the CBUs beneath it.</summary>
    Task<OrgHierarchy> GetHierarchyAsync(CancellationToken cancellationToken);
}

/// <summary>Field teams as other modules need them. Implemented by Auth.</summary>
public interface IOrgDirectory
{
    /// <summary>The named teams, keyed by id. Unknown ids are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, OrgTeamInfo>> GetTeamsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken);

    /// <summary>Every active team with its coverage, for deciding who may take a piece of work.</summary>
    Task<IReadOnlyList<OrgTeamCoverage>> GetActiveTeamsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The names of org units at one level — one of <see cref="OrgLevels"/> or
    /// <see cref="OrgUnitLevels.Department"/> — keyed by code, case-insensitively. Unknown codes are
    /// simply absent. Work carries codes; this is for printing them as a person reads them.
    /// </summary>
    Task<IReadOnlyDictionary<string, OrgUnitName>> GetUnitNamesAsync(
        string level,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken);
}

/// <summary>The unit levels <see cref="IOrgDirectory.GetUnitNamesAsync"/> names beyond the territory ones.</summary>
public static class OrgUnitLevels
{
    public const string Department = nameof(Department);
}

public sealed record OrgUnitName(string Code, string NameEn, string NameAr);

public sealed record OrgTeamInfo(Guid Id, string Name, string? Mobile, bool IsActive);

public sealed record OrgTeamCoverage(OrgTeamInfo Team, OrgScopeSet Scope);
