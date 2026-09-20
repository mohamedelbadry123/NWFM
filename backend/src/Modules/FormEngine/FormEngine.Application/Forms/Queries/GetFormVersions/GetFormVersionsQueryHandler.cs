using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Queries.GetFormVersions;

public sealed class GetFormVersionsQueryHandler(IFormEngineDbContext context)
    : IRequestHandler<GetFormVersionsQuery, Result<IReadOnlyList<FormVersionSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<FormVersionSummaryDto>>> Handle(GetFormVersionsQuery request, CancellationToken ct)
    {
        if (!await context.FormDefinitions.AnyAsync(x => x.Id == request.FormDefinitionId, ct))
        {
            return Result.Failure<IReadOnlyList<FormVersionSummaryDto>>(FormEngineErrors.Form.NotFound);
        }

        IReadOnlyList<FormVersionSummaryDto> versions = await context.FormVersions
            .AsNoTracking()
            .Where(x => x.FormDefinitionId == request.FormDefinitionId)
            .OrderByDescending(x => x.VersionNo)
            .ThenBy(x => x.TargetClient)
            .Select(x => new FormVersionSummaryDto
            {
                Id = x.Id,
                VersionNo = x.VersionNo,
                TargetClient = x.TargetClient,
                PublishedBy = x.PublishedBy,
                PublishedAt = x.PublishedAt,
            })
            .ToListAsync(ct);

        return Result.Success(versions);
    }
}
