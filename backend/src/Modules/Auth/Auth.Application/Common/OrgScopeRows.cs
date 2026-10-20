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
                DepartmentCodes = s.Departments.Select(d => d.DepartmentCode).ToList(),
                ActivityTypeCodes = s.ActivityTypes.Select(a => a.ActivityTypeCode).ToList()
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
            .Select(s => new
            {
                s.OwnerId,
                Scope = new OrgScopeAssignmentDto
                {
                    Level = s.Level,
                    Code = s.Code,
                    DepartmentCodes = s.Departments.Select(d => d.DepartmentCode).ToList(),
                    ActivityTypeCodes = s.ActivityTypes.Select(a => a.ActivityTypeCode).ToList()
                }
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OrgScopeAssignmentDto>)g.Select(r => r.Scope).ToList(),
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
        var prepared = Prepare(ownerType, ownerId, scopes);
        if (prepared.IsFailure)
            return Result<bool>.Failure(prepared.Error);

        // Loaded with their department and activity type rows, so those go with them.
        var existing = await db.OrgScopes
            .Include(s => s.Departments)
            .Include(s => s.ActivityTypes)
            .Where(s => s.OwnerType == ownerType && s.OwnerId == ownerId)
            .ToListAsync(ct);

        db.OrgScopes.RemoveRange(existing);
        db.OrgScopes.AddRange(prepared.Value);

        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>Validate the complete request before account or role changes are persisted.</summary>
    public static Result<bool> Validate(
        string ownerType, string ownerId, IReadOnlyList<OrgScopeAssignmentDto>? scopes)
    {
        var prepared = Prepare(ownerType, ownerId, scopes);
        return prepared.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(prepared.Error);
    }

    private static Result<IReadOnlyList<OrgScope>> Prepare(
        string ownerType, string ownerId, IReadOnlyList<OrgScopeAssignmentDto>? scopes)
    {
        var replacements = new List<OrgScope>();

        foreach (var scope in scopes ?? [])
        {
            if (scope is null)
                return Result<IReadOnlyList<OrgScope>>.Failure(
                    new Error("Auth.InvalidScope", "A scope entry must not be null."));
            try
            {
                replacements.Add(OrgScope.Create(
                    ownerType, ownerId, scope.Level, scope.Code, scope.DepartmentCodes, scope.ActivityTypeCodes));
            }
            catch (DomainException ex)
            {
                return Result<IReadOnlyList<OrgScope>>.Failure(new Error("Auth.InvalidScope", ex.Message));
            }
        }
        return Result<IReadOnlyList<OrgScope>>.Success(replacements);
    }
}
