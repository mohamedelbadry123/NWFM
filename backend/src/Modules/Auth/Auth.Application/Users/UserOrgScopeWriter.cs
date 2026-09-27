using Auth.Application.Common;
using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using Auth.Domain.Constants;
using NWFM.Shared.Results;

namespace Auth.Application.Users;

/// <summary>A user's own org scope rows. Team scopes are written by the teams screen.</summary>
internal static class UserOrgScopeWriter
{
    public static Task<IReadOnlyList<OrgScopeAssignmentDto>> ListAsync(
        IAuthDbContext db, string userId, CancellationToken ct) =>
        OrgScopeRows.ListAsync(db, OrgScopeOwnerTypes.User, userId, ct);

    public static Task<Result<bool>> ReplaceAsync(
        IAuthDbContext db,
        string userId,
        IReadOnlyList<OrgScopeAssignmentDto>? scopes,
        CancellationToken ct) =>
        OrgScopeRows.ReplaceAsync(db, OrgScopeOwnerTypes.User, userId, scopes, ct);
}
