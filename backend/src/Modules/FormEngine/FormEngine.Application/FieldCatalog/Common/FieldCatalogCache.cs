using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.FieldCatalog.Models;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Options;

namespace FormEngine.Application.FieldCatalog.Common;

/// <summary>
/// Single read path for the cached field catalog. The catalog is no longer a table of its own: it is
/// every form's field registry, grouped by data name. Both catalog queries come through here, so the
/// one cached entry can never be written from two projections that have drifted apart. Publishing
/// evicts it, which is the only thing that adds fields.
/// </summary>
internal static class FieldCatalogCache
{
    public static ValueTask<List<FieldCatalogItemDto>> GetAllAsync(
        IFormEngineDbContext context,
        ICacheService cache,
        CacheSettings cacheSettings,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            CacheKeys.FormEngine.FieldCatalog,
            ct => LoadAllAsync(context, ct),
            cacheSettings.ToLookupEntryOptions(),
            cancellationToken);

    private static async ValueTask<List<FieldCatalogItemDto>> LoadAllAsync(
        IFormEngineDbContext context,
        CancellationToken cancellationToken)
    {
        // Companions are columns, not names anyone types: offering "material_other" in the builder
        // would invite a field that collides with the choice field's own free-text column.
        var fields = await context.FormFields
            .AsNoTracking()
            .Where(f => !f.IsCompanion)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id,
                f.FormDefinitionId,
                f.DataName,
                f.FieldType,
                f.LabelEn,
                f.LabelAr,
            })
            .ToListAsync(cancellationToken);

        // Grouped in memory: the names are few, and a GroupBy that must also pick the first row's
        // labels does not translate to SQL cleanly.
        return fields
            .GroupBy(f => f.DataName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var types = group.Select(f => f.FieldType).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                return new FieldCatalogItemDto
                {
                    Id = first.Id,
                    DataName = first.DataName,
                    FieldType = first.FieldType,
                    LabelEn = group.Select(f => f.LabelEn).LastOrDefault(label => label is not null),
                    LabelAr = group.Select(f => f.LabelAr).LastOrDefault(label => label is not null),
                    FormCount = group.Select(f => f.FormDefinitionId).Distinct().Count(),
                    HasTypeConflict = types.Count > 1,
                    FieldTypes = types,
                };
            })
            .OrderBy(entry => entry.DataName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Matches a catalog entry against a free-text search, the way both queries do it.</summary>
    public static bool Matches(FieldCatalogItemDto entry, string term) =>
        entry.DataName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (entry.LabelEn is not null && entry.LabelEn.Contains(term, StringComparison.OrdinalIgnoreCase))
        || (entry.LabelAr is not null && entry.LabelAr.Contains(term, StringComparison.OrdinalIgnoreCase));
}
