namespace Tenancy.Application.Constants;

public static class TenancyErrors
{
    public static readonly Error SupportGrantNotFound = new("Support.GrantNotFound", "Support grant not found.", 404);

    public static readonly Error SupportSessionNotFound = new("Support.SessionNotFound", "Support session not found.", 404);

    public static readonly Error NotASupportSession = new("Support.NotASession", "This sign-in is not a support session.", 400);

    public static readonly Error HandoffInvalid = new(
        TenancyErrorCodes.SupportAccessNotGranted,
        "The support handoff link is invalid, expired or already used.",
        401);

    public static Error GrantTooLong(int maxHours) =>
        new("Support.GrantTooLong", $"Support access lasts at most {maxHours} hours.", 400);
}
