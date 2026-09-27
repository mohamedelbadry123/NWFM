using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Uploads.Common;
using FormEngine.Application.Uploads.Models;
using FormEngine.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Options;
using NWFM.Shared.Results;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Uploads.Commands.UploadFormFile;

public sealed class UploadFormFileCommandHandler(
    IFormEngineDbContext context,
    IFileStorage fileStorage,
    ICurrentUser user,
    IOptions<FileStorageOptions> options)
    : IRequestHandler<UploadFormFileCommand, Result<UploadedFileDto>>
{
    public async Task<Result<UploadedFileDto>> Handle(UploadFormFileCommand request, CancellationToken ct)
    {
        var settings = options.Value;
        var maxBytes = (long)settings.MaxFileSizeMb * FormFileRules.BytesPerMb;

        if (request.SizeBytes > maxBytes)
        {
            return Result.Failure<UploadedFileDto>(FormEngineErrors.File.TooLarge(settings.MaxFileSizeMb));
        }

        if (!FormFileRules.IsAllowedContentType(request.ContentType, settings.AllowedContentTypes))
        {
            return Result.Failure<UploadedFileDto>(FormEngineErrors.File.ContentTypeNotAllowed(request.ContentType));
        }

        var resolved = await FormSchemaLoader.LoadAsync(context, request.FormDefinitionId, request.VersionNo, ct);

        if (resolved is null)
        {
            return Result.Failure<UploadedFileDto>(
                request.VersionNo is null ? FormEngineErrors.Form.NotFound : FormEngineErrors.Version.NotFound);
        }

        var extension = FormFileRules.SafeExtension(request.FileName);

        // Rejected before anything is written, so a disallowed file never touches disk at all.
        if (!FormFileRules.IsAllowedExtension(resolved.Schema, request.DataName, extension, out var allowed))
        {
            return Result.Failure<UploadedFileDto>(
                FormEngineErrors.File.ExtensionNotAllowed(request.FileName, request.DataName, allowed));
        }

        // The stored name is the file id, so a hostile client-supplied name can never reach disk.
        var fileId = Guid.NewGuid();
        var storedName = fileId.ToString("N") + extension;

        var stored = await fileStorage.SaveAsync(
            request.Content,
            storedName,
            request.ContentType,
            settings.PendingFolder,
            ct);

        if (stored.SizeBytes == 0)
        {
            await fileStorage.DeleteAsync(stored.RelativePath, ct);
            return Result.Failure<UploadedFileDto>(FormEngineErrors.File.Empty);
        }

        if (stored.SizeBytes > maxBytes)
        {
            // The declared size lied; drop what was written rather than keep an over-limit file.
            await fileStorage.DeleteAsync(stored.RelativePath, ct);
            return Result.Failure<UploadedFileDto>(FormEngineErrors.File.TooLarge(settings.MaxFileSizeMb));
        }

        try
        {
            var entry = SubmissionFile.CreatePending(
                fileId,
                resolved.Form.Id,
                resolved.VersionNo,
                request.DataName,
                request.FileName,
                request.ContentType,
                stored.SizeBytes,
                stored.RelativePath,
                request.ContextType,
                request.ContextId,
                user.Id);

            context.SubmissionFiles.Add(entry);
            await context.SaveChangesAsync(ct);

            return Result.Success(new UploadedFileDto(
                entry.Id,
                entry.RelativePath,
                entry.FileName,
                entry.ContentType,
                entry.SizeBytes));
        }
        catch (DomainException ex)
        {
            await fileStorage.DeleteAsync(stored.RelativePath, ct);
            return Result.Failure<UploadedFileDto>(FormEngineErrors.Form.Invalid(ex.Message));
        }
    }
}
