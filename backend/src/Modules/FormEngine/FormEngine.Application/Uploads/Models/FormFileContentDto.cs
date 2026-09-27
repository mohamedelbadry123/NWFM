namespace FormEngine.Application.Uploads.Models;

/// <summary>The open stream plus what the response needs to describe it. The caller owns the stream.</summary>
public sealed record FormFileContentDto(Stream Content, string ContentType, string FileName);
