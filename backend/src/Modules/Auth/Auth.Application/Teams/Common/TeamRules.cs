namespace Auth.Application.Teams.Common;

/// <summary>Limits shared by the team commands, matching the <c>Auth.Teams</c> columns and the login rules.</summary>
internal static class TeamRules
{
    public const int NameMaxLength = 200;
    public const int MobileMaxLength = 20;
    public const int UserCodeMaxLength = 50;
    public const int PasswordMinLength = 8;

    public const string ScopeRequiredMessage =
        "A team needs at least one scope: its crew cannot sign in without one, and no task can be assigned to it.";
}
