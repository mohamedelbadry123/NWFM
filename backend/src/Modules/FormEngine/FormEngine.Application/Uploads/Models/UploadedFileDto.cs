namespace FormEngine.Application.Uploads.Models;

/// <summary>
/// What the client keeps in the field value: enough to render the file and to reference it on
/// submit. The submission column stores an array of these, never the bytes.
/// </summary>
public sealed record UploadedFileDto(Guid FileId, string Path, string Name, string Type, long Size);
