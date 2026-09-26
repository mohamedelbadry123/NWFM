using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace Auth.Application.Common;

/// <summary>Reads and replaces the org scope rows of one owner — a user or a team.</summary>
internal static class OrgScopeRows
{
    public static async Task<IReadOnlyList<OrgScopeAssignmentDto>> ListAsync(
        IAuthDbContext db, string ownerType, string ownerId, CancellationToken ct) =>
        await db.OrgScopes
            .AsNoTracking()
            .Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId)
            .Select(s => new OrgScopeAssignmentDto
            {
                Level = s.Level,
                Code = s.Code,
                DepartmentId = s.DepartmentId
            })
            .ToListAsync(ct);

    /// <summary>Every listed owner's rows, in one read, keyed by owner id.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<OrgScopeAssignmentDto>>> ListManyAsync(
        IAuthDbContext db, string ownerType, IReadOnlyCollection<string> ownerIds, CancellationToken ct)
    {
        if (ownerIds.Count == 0)
            return new Dictionary<string, IReadOnlyList<OrgScopeAssignmentDto>>();

        var ids = ownerIds.ToList();

        var rows = await db.OrgScopes
            .AsNoTracking()
            .Where(s => s.OwnerType == ownerType && ids.Contains(s.OwnerId))
            .Select(s => new { s.OwnerId, s.Level, s.Code, s.DepartmentId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OrgScopeAssignmentDto>)g
                    .Select(r => new OrgScopeAssignmentDto { Level = r.Level, Code = r.Code, DepartmentId = r.DepartmentId })
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the owner's rows with <paramref name="scopes"/> and saves. A row the domain refuses —
    /// a level without a code, say — fails the whole replacement before anything is saved.
    /// </summary>
    public static async Task<Result<bool>> ReplaceAsync(
        IAuthDbContext db,
        string ownerType,
        string ownerId,
        IReadOnlyList<OrgScopeAssignmentDto>? scopes,
        CancellationToken ct)
    {
        var replacements = new List<OrgScope>();

        foreach (var scope in scopes ?? [])
        {
            try
            {
                var departmentId = string.IsNullOrWhiteSpace(scope.DepartmentId)
                    ? null
                    : scope.DepartmentId.Trim();

                replacements.Add(OrgScope.Create(ownerType, ownerId, scope.Level, scope.Code, departmentId));
            }
            catch (DomainException ex)
            {
                return Result<bool>.Failure(new Error("Auth.InvalidScope", ex.Message));
            }
        }

        var existing = await db.OrgScopes
            .Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId)
            .ToListAsync(ct);

        db.OrgScopes.RemoveRange(existing);
        db.OrgScopes.AddRange(replacements);

        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
