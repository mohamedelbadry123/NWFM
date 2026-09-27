using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace NWFM.Shared.Storage;

/// <summary>
/// Local-disk <see cref="IFileStorage"/>. Everything is written under a single configured root,
/// and every path is re-resolved against that root before use, so a crafted file name or relative
/// path can never escape it.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920;

    private readonly string _root;

    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.Root;
        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, string subPath, CancellationToken cancellationToken)
    {
        var relativePath = Combine(subPath, Path.GetFileName(fileName));
        var fullPath = ResolveInsideRoot(relativePath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var target = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);

        return new StoredFile(relativePath, target.Length);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveInsideRoot(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveInsideRoot(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The stored file no longer exists.", relativePath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<string> MoveAsync(string sourceRelativePath, string destinationSubPath, string fileName, CancellationToken cancellationToken)
    {
        var sourceFullPath = ResolveInsideRoot(sourceRelativePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException("The stored file no longer exists.", sourceRelativePath);
        }

        var destinationRelativePath = Combine(destinationSubPath, Path.GetFileName(fileName));
        var destinationFullPath = ResolveInsideRoot(destinationRelativePath);

        var directory = Path.GetDirectoryName(destinationFullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(sourceFullPath, destinationFullPath, overwrite: true);

        return Task.FromResult(destinationRelativePath);
    }

    private static string Combine(string subPath, string fileName) =>
        string.IsNullOrWhiteSpace(subPath) ? fileName : $"{subPath.Trim('/', '\\')}/{fileName}";

    /// <summary>Resolves a relative path against the root and rejects anything that escapes it.</summary>
    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("A storage path is required.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Rejected storage path '{relativePath}' outside the configured root.");
        }

        return fullPath;
    }
}
