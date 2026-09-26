using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Queries.GetForms;

public sealed class GetFormsQueryHandler(IFormEngineDbContext context)
    : IRequestHandler<GetFormsQuery, Result<PaginatedResult<FormListItemDto>>>
{
    public async Task<Result<PaginatedResult<FormListItemDto>>> Handle(GetFormsQuery request, CancellationToken ct)
    {
        var query = context.FormDefinitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim();
            query = query.Where(x => x.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            query = query.Where(x => x.Status == status);
        }
        else if (request.ExcludeArchived)
        {
            query = query.Where(x => x.Status != FormStatuses.Archived);
        }

        if (!string.IsNullOrWhiteSpace(request.DepartmentCode))
        {
            var department = request.DepartmentCode.Trim();
            query = query.Where(x => x.DepartmentCode == department);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(x =>
                x.Code.Contains(term) ||
                x.NameEn.Contains(term) ||
                x.NameAr.Contains(term));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Code)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new FormListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                NameEn = x.NameEn,
                NameAr = x.NameAr,
                Category = x.Category,
                Status = x.Status,
                DepartmentCode = x.DepartmentCode,
                CurrentVersionNo = x.CurrentVersionNo,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(ct);

        return Result.Success(
            new PaginatedResult<FormListItemDto>(items, totalCount, request.PageNumber, request.PageSize));
    }
}
