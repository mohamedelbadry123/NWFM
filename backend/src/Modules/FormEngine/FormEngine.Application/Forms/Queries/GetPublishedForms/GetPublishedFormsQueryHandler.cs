using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Queries.GetPublishedForms;

public sealed class GetPublishedFormsQueryHandler(IFormEngineDbContext context)
    : IRequestHandler<GetPublishedFormsQuery, Result<IReadOnlyList<PublishedFormDto>>>
{
    public async Task<Result<IReadOnlyList<PublishedFormDto>>> Handle(GetPublishedFormsQuery request, CancellationToken ct)
    {
        // A draft revision of a published form is still fillable, against its last published version.
        var query = context.FormDefinitions
            .AsNoTracking()
            .Where(x => x.CurrentVersionNo != null
                && x.Status != FormStatuses.Deprecated
                && x.Status != FormStatuses.Archived);

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim();
            query = query.Where(x => x.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(x =>
                x.Code.Contains(term) ||
                x.NameEn.Contains(term) ||
                x.NameAr.Contains(term));
        }

        var forms = await query
            .OrderBy(x => x.Code)
            .Take(request.Take)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.NameEn,
                x.NameAr,
                x.Category,
                x.Status,
                x.DepartmentCode,
                CurrentVersionNo = x.CurrentVersionNo!.Value,
            })
            .ToListAsync(ct);

        var ids = forms.Select(x => x.Id).ToList();

        var versions = await context.FormVersions
            .AsNoTracking()
            .Where(v => ids.Contains(v.FormDefinitionId) && v.TargetClient == FormTargetClients.Formly)
            .Select(v => new { v.FormDefinitionId, v.VersionNo })
            .ToListAsync(ct);

        var versionsByForm = versions
            .GroupBy(v => v.FormDefinitionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.Select(v => v.VersionNo).Distinct().OrderByDescending(n => n).ToList());

        IReadOnlyList<PublishedFormDto> result = forms
            .Select(x => new PublishedFormDto
            {
                Id = x.Id,
                Code = x.Code,
                NameEn = x.NameEn,
                NameAr = x.NameAr,
                Category = x.Category,
                Status = x.Status,
                DepartmentCode = x.DepartmentCode,
                CurrentVersionNo = x.CurrentVersionNo,
                VersionNos = versionsByForm.TryGetValue(x.Id, out var numbers) ? numbers : [x.CurrentVersionNo],
            })
            .ToList();

        return Result.Success(result);
    }
}
