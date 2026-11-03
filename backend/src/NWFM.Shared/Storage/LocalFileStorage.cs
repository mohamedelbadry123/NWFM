using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Options;

namespace NWFM.Shared.Storage;

/// <summary>
/// Local-disk <see cref="IFileStorage"/>. Every tenant writes under its own root
/// (<see cref="TenantStorageLayout.TenantRoot"/>), bound to it by a marker file, and every path is
/// re-resolved against that root before use, so a crafted file name or relative path can never escape it.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920;
    private const string TenantBindingFile = ".nwfm-tenant";

    private readonly string _storageRoot;
    private readonly string _contentRoot;
    private readonly long _maximumBytes;
    private readonly ITenantContext _tenants;
    private readonly ConcurrentDictionary<Guid, string> _boundRoots = new();
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment, ITenantContext tenants)
    {
        if (options.Value.MaxFileSizeMb <= 0)
            throw new InvalidOperationException("File storage requires a positive file size limit.");

        _maximumBytes = (long)options.Value.MaxFileSizeMb * 1024 * 1024;
        _storageRoot = options.Value.Root;
        _contentRoot = environment.ContentRootPath;
        _tenants = tenants;
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, string subPath, CancellationToken cancellationToken)
    {
        var relativePath = Combine(subPath, SafeFileName(fileName));
        var fullPath = ResolveInsideRoot(relativePath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var created = false;
        try
        {
            // An upload must never replace an existing object. Read at most one byte beyond
            // the limit, even when the sender lies about Content-Length or never reaches EOF.
            await using var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            created = true;
            var buffer = new byte[BufferSize];
            long written = 0;
            while (true)
            {
                var count = (int)Math.Min(buffer.Length, _maximumBytes - written + 1);
                var read = await content.ReadAsync(buffer.AsMemory(0, count), cancellationToken);
                if (read == 0) break;
                if (written + read > _maximumBytes) throw new FileStorageLimitException();
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }
            await target.FlushAsync(cancellationToken);
            return new StoredFile(relativePath, written);
        }
        catch
        {
            if (created) File.Delete(fullPath);
            throw;
        }
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

        var destinationRelativePath = Combine(destinationSubPath, SafeFileName(fileName));
        var destinationFullPath = ResolveInsideRoot(destinationRelativePath);

        var directory = Path.GetDirectoryName(destinationFullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(sourceFullPath, destinationFullPath, overwrite: false);

        return Task.FromResult(destinationRelativePath);
    }

    private static string Combine(string subPath, string fileName) =>
        string.IsNullOrWhiteSpace(subPath) ? fileName : $"{subPath}/{fileName}";

    private static string SafeFileName(string fileName) => Path.GetFileName(fileName.Replace('\\', '/'));

    /// <summary>The ambient tenant's root, created and bound on first use. An unbound flow has no storage.</summary>
    private string CurrentRoot()
    {
        var tenant = _tenants.Current ?? throw TenantAccessException.Required();
        return _boundRoots.GetOrAdd(tenant.Id, _ =>
        {
            var root = Path.TrimEndingDirectorySeparator(TenantStorageLayout.TenantRoot(_storageRoot, _contentRoot, tenant));
            RejectRedirectedAncestors(root);
            Directory.CreateDirectory(root);
            RejectRedirectedAncestors(root);
            BindTenant(root, tenant.Id);
            return root;
        });
    }

    /// <summary>Resolves a relative path against the tenant root and rejects anything that escapes it.</summary>
    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || relativePath.StartsWith('/') || relativePath.StartsWith('\\') || relativePath.Contains(':'))
        {
            throw new InvalidOperationException("A relative storage path is required.");
        }

        var parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."
            || part.StartsWith(".nwfm-", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The storage path contains an invalid component.");

        var root = CurrentRoot();
        var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidOperationException("The storage path is outside the tenant root.");
        }

        // Lexical containment alone does not stop a symlink/junction under the storage root.
        RejectRedirectedAncestors(root);
        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            RejectLink(current);
        }
        return fullPath;
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path))
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Storage paths cannot contain symbolic links or junctions.");
    }

    private static void RejectRedirectedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            RejectLink(directory.FullName);
    }

    private static void BindTenant(string root, Guid tenantId)
    {
        var marker = Path.Combine(root, TenantBindingFile);
        RejectLink(marker);
        if (!File.Exists(marker))
        {
            // Publish a complete marker atomically; simultaneous first writes must not observe
            // an empty file or overwrite another tenant's binding.
            var temporary = Path.Combine(root, ".nwfm-binding-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(temporary, tenantId.ToString("D"));
                try { File.Move(temporary, marker, overwrite: false); }
                catch (IOException) when (File.Exists(marker)) { }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        if (!Guid.TryParse(File.ReadAllText(marker).Trim(), out var boundTenant) || boundTenant != tenantId)
            throw new InvalidOperationException("The file storage root belongs to another tenant or has an invalid tenant binding.");
    }
}
