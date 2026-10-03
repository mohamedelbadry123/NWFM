using Auth.Application.Common.Interfaces;
using Auth.Application.Lookups.Queries;
using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups;

/// <summary>Shared by the lookup commands for activity types and the sources allowed to create them.</summary>
internal static class ActivityLookups
{
    public static readonly Error SourcesRequired =
        new("Auth.ActivitySourcesRequired", "Choose at least one source allowed to create this activity type.");

    public static readonly Error SourceInvalid =
        new("Auth.ActivitySourceInvalid", "Every source must be an active activity source.");

    /// <summary>
    /// An activity type needs at least one source, and each must exist. A source added now must be
    /// active; one the type already had may have been deactivated since and does not block an edit.
    /// </summary>
    public static async Task<Error?> ValidateSourcesAsync(
        IAuthDbContext db, IReadOnlyList<string>? sourceCodes, IReadOnlyCollection<string> current, CancellationToken ct)
    {
        var codes = (sourceCodes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0)
            return SourcesRequired;

        var found = await db.ActivitySources.AsNoTracking().Where(s => codes.Contains(s.Code))
            .Select(s => new { s.Code, s.IsActive }).ToListAsync(ct);

        foreach (var code in codes)
        {
            var source = found.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
            if (source is null || (!source.IsActive && !current.Contains(code, StringComparer.OrdinalIgnoreCase)))
                return SourceInvalid;
        }

        return null;
    }

    public static LookupItemDto Map(FieldActivityType x) => new()
    {
        Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive,
        SourceCodes = x.Sources.Select(s => s.SourceCode).OrderBy(c => c).ToList()
    };

    public static LookupItemDto Map(ActivitySource x) => new()
    {
        Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive, Kind = x.Kind, Url = x.Url
    };
}
