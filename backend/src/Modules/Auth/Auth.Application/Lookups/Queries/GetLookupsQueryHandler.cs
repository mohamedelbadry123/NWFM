using Auth.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Queries;

public sealed class GetLookupsQueryHandler(IAuthDbContext context)
    : IRequestHandler<GetLookupsQuery, Result<PaginatedResult<LookupItemDto>>>
{
    public async Task<Result<PaginatedResult<LookupItemDto>>> Handle(
        GetLookupsQuery request, CancellationToken ct)
    {
        IQueryable<LookupItemDto> query = request.LookupType switch
        {
            "Department" => context.Departments.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive
            }),
            "FieldActivityType" => context.FieldActivityTypes.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive
            }),
            "ActivitySource" => context.ActivitySources.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive, Kind = x.Kind, Url = x.Url
            }),
            "Cluster" => context.Clusters.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive
            }),
            "Cbu" => context.Cbus.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive,
                ParentCode = x.ClusterCode
            }),
            "Branch" => context.Branches.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive,
                ParentCode = x.CbuCode
            }),
            "OperationArea" => context.OperationAreas.AsNoTracking().Select(x => new LookupItemDto
            {
                Id = x.Id, Code = x.Code, NameEn = x.NameEn, NameAr = x.NameAr, IsActive = x.IsActive,
                ParentCode = x.CbuCode
            }),
            _ => throw new ArgumentException($"Unknown lookup type: {request.LookupType}")
        };

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.NameEn.Contains(term) || x.NameAr.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.ParentCode))
        {
            var parent = request.ParentCode.Trim();
            query = query.Where(x => x.ParentCode == parent);
        }

        if (request.IsActive is bool isActive)
            query = query.Where(x => x.IsActive == isActive);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Code)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        if (request.LookupType == "FieldActivityType" && items.Count > 0)
        {
            var ids = items.Select(x => x.Id).ToList();
            var sources = await context.FieldActivityTypeSources.AsNoTracking()
                .Where(s => ids.Contains(s.FieldActivityTypeId))
                .Select(s => new { s.FieldActivityTypeId, s.SourceCode })
                .ToListAsync(ct);
            foreach (var item in items)
                item.SourceCodes = sources.Where(s => s.FieldActivityTypeId == item.Id).Select(s => s.SourceCode).OrderBy(c => c).ToList();
        }

        return Result<PaginatedResult<LookupItemDto>>.Success(
            new PaginatedResult<LookupItemDto>(items, totalCount, request.PageNumber, request.PageSize));
    }
}
