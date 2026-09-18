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

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Code)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Result<PaginatedResult<LookupItemDto>>.Success(
            new PaginatedResult<LookupItemDto>(items, totalCount, request.PageNumber, request.PageSize));
    }
}
