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
}
