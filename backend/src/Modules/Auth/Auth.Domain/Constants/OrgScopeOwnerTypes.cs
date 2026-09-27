namespace Auth.Domain.Constants;

public static class OrgScopeOwnerTypes
{
    public const string User = nameof(User);
    public const string Team = nameof(Team);

    private static readonly HashSet<string> Defined = new(StringComparer.OrdinalIgnoreCase)
    {
        User, Team
    };

    public static bool IsDefined(string? type) =>
        type is not null && Defined.Contains(type);

    /// <summary>
    /// The <c>OwnerId</c> a team's scope rows are stored under. One format, written and read through
    /// here, so a row written by the teams screen is the row login and the scope reader look for.
    /// </summary>
    public static string TeamOwnerId(Guid teamId) => teamId.ToString("D");

    /// <summary>The <c>OwnerId</c> a user's scope rows are stored under.</summary>
    public static string UserOwnerId(Guid userId) => userId.ToString("D");
}
