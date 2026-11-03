namespace Tenancy.Application.SupportAccess.Models;

public static class SupportAccessMapping
{
    public static SupportAccessScope ToSupportAccessScope(this string scope) =>
        string.Equals(scope, SupportScopes.Full, StringComparison.OrdinalIgnoreCase)
            ? SupportAccessScope.Full
            : SupportAccessScope.ReadOnly;

    public static string ToClaimValue(this SupportAccessScope scope) =>
        scope == SupportAccessScope.Full ? SupportScopes.Full : SupportScopes.ReadOnly;

    public static SupportAccessGrantDto ToDto(this SupportAccessGrant grant, DateTime utcNow) => new()
    {
        Id = grant.Id,
        Scope = grant.Scope.ToClaimValue(),
        Reason = grant.Reason,
        GrantedByUserName = grant.GrantedByUserName,
        CreatedAtUtc = grant.CreatedAtUtc,
        ExpiresAtUtc = grant.ExpiresAtUtc,
        RevokedAtUtc = grant.RevokedAtUtc,
        IsActive = grant.IsActive(utcNow)
    };

    public static SupportAccessSessionDto ToDto(this SupportSession session, DateTime utcNow) => new()
    {
        Id = session.Id,
        GrantId = session.GrantId,
        PlatformUserName = session.PlatformUserName,
        Scope = session.Scope.ToClaimValue(),
        TicketReference = session.TicketReference,
        ImpersonatedUserName = session.ImpersonatedUserName,
        StartedAtUtc = session.StartedAtUtc,
        ExpiresAtUtc = session.ExpiresAtUtc,
        EndedAtUtc = session.EndedAtUtc,
        IsActive = session.IsActive(utcNow)
    };
}
