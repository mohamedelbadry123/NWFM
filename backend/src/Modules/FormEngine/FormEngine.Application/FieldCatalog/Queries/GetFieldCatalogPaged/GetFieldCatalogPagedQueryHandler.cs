using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.FieldCatalog.Common;
using FormEngine.Application.FieldCatalog.Models;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Options;
using NWFM.Shared.Results;

namespace FormEngine.Application.FieldCatalog.Queries.GetFieldCatalogPaged;

/// <summary>Serves the grid from the same cached list as the autocomplete, then pages in memory.</summary>
public sealed class GetFieldCatalogPagedQueryHandler(
    IFormEngineDbContext context,
    ICacheService cache,
    IOptions<CacheSettings> cacheSettings)
    : IRequestHandler<GetFieldCatalogPagedQuery, Result<PaginatedResult<FieldCatalogItemDto>>>
{
    public async Task<Result<PaginatedResult<FieldCatalogItemDto>>> Handle(
        GetFieldCatalogPagedQuery request,
        CancellationToken ct)
    {
        var entries = await FieldCatalogCache.GetAllAsync(context, cache, cacheSettings.Value, ct);

        IEnumerable<FieldCatalogItemDto> filtered = entries;

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            filtered = filtered.Where(entry => FieldCatalogCache.Matches(entry, term));
        }

        var matches = filtered.ToList();

        var items = matches
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return Result.Success(
            new PaginatedResult<FieldCatalogItemDto>(items, matches.Count, request.PageNumber, request.PageSize));
    }
}
