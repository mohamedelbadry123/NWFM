using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Users;

internal static class UserOrgScopeWriter
{
    public static async Task<IReadOnlyList<OrgScopeAssignmentDto>> ListAsync(
        IAuthDbContext db, string userId, CancellationToken ct) =>
        await db.OrgScopes
            .AsNoTracking()
            .Where(s => s.OwnerType == OrgScopeOwnerTypes.User && s.OwnerId == userId)
            .Select(s => new OrgScopeAssignmentDto
            {
                Level = s.Level,
                Code = s.Code,
                DepartmentId = s.DepartmentId
            })
            .ToListAsync(ct);

    public static async Task<Result<bool>> ReplaceAsync(
        IAuthDbContext db,
        string userId,
        IReadOnlyList<OrgScopeAssignmentDto>? scopes,
        CancellationToken ct)
    {
        var existing = await db.OrgScopes
            .Where(s => s.OwnerType == OrgScopeOwnerTypes.User && s.OwnerId == userId)
            .ToListAsync(ct);
        db.OrgScopes.RemoveRange(existing);

        foreach (var scope in scopes ?? [])
        {
            try
            {
                var departmentId = string.IsNullOrWhiteSpace(scope.DepartmentId)
                    ? null
                    : scope.DepartmentId.Trim();
                db.OrgScopes.Add(OrgScope.Create(
                    OrgScopeOwnerTypes.User,
                    userId,
                    scope.Level,
                    scope.Code,
                    departmentId));
            }
            catch (DomainException ex)
            {
                return Result<bool>.Failure(new Error("Auth.InvalidScope", ex.Message));
            }
        }

        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
