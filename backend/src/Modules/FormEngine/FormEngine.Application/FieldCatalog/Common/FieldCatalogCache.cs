using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.FieldCatalog.Models;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Options;

namespace FormEngine.Application.FieldCatalog.Common;

/// <summary>
/// Single read path for the cached field catalog. Both catalog queries come through here, so the one
/// cached entry can never be written from two projections that have drifted apart. Publishing evicts
/// it, which is the only thing that adds entries.
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
        CancellationToken cancellationToken) =>
        await context.FieldCatalog
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DataName)
            .Select(c => new FieldCatalogItemDto
            {
                Id = c.Id,
                DataName = c.DataName,
                FieldType = c.FieldType,
                LabelEn = c.LabelEn,
                LabelAr = c.LabelAr,
                Description = c.Description,
            })
            .ToListAsync(cancellationToken);

    /// <summary>Matches a catalog entry against a free-text search, the way both queries do it.</summary>
    public static bool Matches(FieldCatalogItemDto entry, string term) =>
        entry.DataName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (entry.LabelEn is not null && entry.LabelEn.Contains(term, StringComparison.OrdinalIgnoreCase))
        || (entry.LabelAr is not null && entry.LabelAr.Contains(term, StringComparison.OrdinalIgnoreCase))
        || (entry.Description is not null && entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
}
