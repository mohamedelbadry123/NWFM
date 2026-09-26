using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Uploads.Common;
using FormEngine.Application.Uploads.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Uploads.Queries.GetFormFile;

public sealed class GetFormFileQueryHandler(
    IFormEngineDbContext context,
    IFileStorage fileStorage,
    ICurrentUser user)
    : IRequestHandler<GetFormFileQuery, Result<FormFileContentDto>>
{
    public async Task<Result<FormFileContentDto>> Handle(GetFormFileQuery request, CancellationToken ct)
    {
        var file = await context.SubmissionFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.FileId && x.IsActive, ct);

        if (file is null)
        {
            return Result.Failure<FormFileContentDto>(FormEngineErrors.File.NotFound);
        }

        if (!FormFileAccess.CanRead(file, user))
        {
            return Result.Failure<FormFileContentDto>(FormEngineErrors.File.Forbidden);
        }

        try
        {
            var content = await fileStorage.OpenReadAsync(file.RelativePath, ct);
            return Result.Success(new FormFileContentDto(content, file.ContentType, file.FileName));
        }
        catch (FileNotFoundException)
        {
            // The row outlived the bytes — report it as missing rather than a 500.
            return Result.Failure<FormFileContentDto>(FormEngineErrors.File.Missing);
        }
    }
}
