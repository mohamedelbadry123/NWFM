namespace NWFM.Shared.Storage;

/// <summary>The storage stream exceeded the server's configured object size limit.</summary>
public sealed class FileStorageLimitException : IOException
{
    public FileStorageLimitException() : base("The file exceeds the configured storage size limit.") { }
}
