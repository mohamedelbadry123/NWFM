using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.FieldCatalog.Common;
using FormEngine.Application.FieldCatalog.Models;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Options;
using NWFM.Shared.Results;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalog;

/// <summary>
/// The catalog is slow-moving reference data, so the whole table is cached as one entry and filtered
/// in memory rather than queried per keystroke.
/// </summary>
public sealed class GetFieldCatalogQueryHandler(
    IFormEngineDbContext context,
    ICacheService cache,
    IOptions<CacheSettings> cacheSettings)
    : IRequestHandler<GetFieldCatalogQuery, Result<IReadOnlyList<FieldCatalogItemDto>>>
{
    public async Task<Result<IReadOnlyList<FieldCatalogItemDto>>> Handle(GetFieldCatalogQuery request, CancellationToken ct)
    {
        var entries = await FieldCatalogCache.GetAllAsync(context, cache, cacheSettings.Value, ct);

        IEnumerable<FieldCatalogItemDto> filtered = entries;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            filtered = filtered.Where(entry => FieldCatalogCache.Matches(entry, term));
        }

        IReadOnlyList<FieldCatalogItemDto> result = filtered.Take(request.Take).ToList();

        return Result.Success(result);
    }
}
