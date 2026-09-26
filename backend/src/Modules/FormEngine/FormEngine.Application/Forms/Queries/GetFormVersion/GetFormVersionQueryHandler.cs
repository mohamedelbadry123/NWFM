using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Queries.GetFormVersion;

public sealed class GetFormVersionQueryHandler(IFormEngineDbContext context)
    : IRequestHandler<GetFormVersionQuery, Result<FormVersionDto>>
{
    public async Task<Result<FormVersionDto>> Handle(GetFormVersionQuery request, CancellationToken ct)
    {
        var form = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.FormDefinitionId, ct);

        if (form is null)
        {
            return Result.Failure<FormVersionDto>(FormEngineErrors.Form.NotFound);
        }

        var versionNo = request.VersionNo ?? form.CurrentVersionNo;

        if (versionNo is null)
        {
            return Result.Failure<FormVersionDto>(FormEngineErrors.Form.NotPublished);
        }

        var version = await context.FormVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.FormDefinitionId == form.Id
                && x.VersionNo == versionNo
                && x.TargetClient == FormTargetClients.Formly, ct);

        return version is null
            ? Result.Failure<FormVersionDto>(FormEngineErrors.Version.NotFound)
            : Result.Success(version.ToVersionDto(form));
    }
}
