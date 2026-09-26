using Auth.Application.Users.Models;

namespace Auth.Application.Teams.Models;

/// <summary>A field team: the crew record, its login, and the territory it works.</summary>
public sealed class TeamDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string? Mobile { get; init; }
    public bool IsActive { get; init; }

    /// <summary>The code the crew signs in with; null for a team created before it had a login.</summary>
    public string? UserCode { get; init; }

    public string? Email { get; init; }

    /// <summary>Whether the crew's login may sign in. Follows the team's own status.</summary>
    public bool LoginEnabled { get; init; }

    public DateTime? LastActiveAt { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Where the crew works — what login, task eligibility and task visibility are judged against.</summary>
    public IReadOnlyList<OrgScopeAssignmentDto> Scopes { get; init; } = [];
}
