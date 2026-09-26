namespace NWFM.Shared.Constants;

/// <summary>
/// Role names shared across modules. Mirrors the roles the Auth module seeds; declared here so a
/// module can check one without referencing Auth.
/// </summary>
public static class NwfmRoles
{
    /// <summary>Unrestricted: passes every permission check and every module's owner checks.</summary>
    public const string Administrator = nameof(Administrator);
}
