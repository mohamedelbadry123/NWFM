namespace NWFM.Shared.Storage;

/// <summary>Where a saved file landed and how big it turned out to be.</summary>
/// <param name="RelativePath">Storage-root-relative path — the only handle the app persists.</param>
/// <param name="SizeBytes">Bytes actually written.</param>
public sealed record StoredFile(string RelativePath, long SizeBytes);

/// <summary>
/// Binary file storage for uploaded media. Paths handed back are always relative to the configured
/// root, so swapping the local-disk implementation for blob/S3 later needs no change to the rows
/// that reference them.
/// </summary>
public interface IFileStorage
{
    /// <summary>Writes <paramref name="content"/> under <c>{subPath}/{fileName}</c> and returns its relative path.</summary>
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, string subPath, CancellationToken cancellationToken);

    /// <summary>Deletes the file at <paramref name="relativePath"/>; a missing file is not an error.</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>Opens the stored file for reading. Throws <see cref="FileNotFoundException"/> when it is gone.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>Moves a file to a new location and returns its new relative path.</summary>
    Task<string> MoveAsync(string sourceRelativePath, string destinationSubPath, string fileName, CancellationToken cancellationToken);
}
