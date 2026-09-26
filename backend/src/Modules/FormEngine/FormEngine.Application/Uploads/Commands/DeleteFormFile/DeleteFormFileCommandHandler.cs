using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Uploads.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Uploads.Commands.DeleteFormFile;

public sealed class DeleteFormFileCommandHandler(
    IFormEngineDbContext context,
    IFileStorage fileStorage,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<DeleteFormFileCommand, Result>
{
    public async Task<Result> Handle(DeleteFormFileCommand request, CancellationToken ct)
    {
        var file = await context.SubmissionFiles.FirstOrDefaultAsync(x => x.Id == request.FileId, ct);

        if (file is null || !file.IsActive)
        {
            return Result.Failure(FormEngineErrors.File.NotFound);
        }

        if (!FormFileAccess.CanManage(file, user))
        {
            return Result.Failure(FormEngineErrors.File.Forbidden);
        }

        if (!file.IsPending)
        {
            return Result.Failure(FormEngineErrors.File.NotDeletable);
        }

        // A migrated file's bytes belong to an imported archive and may be shared between records.
        // Dropping our reference is the most this may ever do to it.
        if (file.IsMigrated)
        {
            file.Deactivate(timeProvider.GetUtcNow().UtcDateTime);
        }
        else
        {
            await fileStorage.DeleteAsync(file.RelativePath, ct);
            context.SubmissionFiles.Remove(file);
        }

        await context.SaveChangesAsync(ct);

        return Result.Success();
    }
}
