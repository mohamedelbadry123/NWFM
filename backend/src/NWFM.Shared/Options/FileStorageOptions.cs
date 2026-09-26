namespace NWFM.Shared.Options;

/// <summary>
/// Binds the <c>FileStorage</c> configuration section that drives <c>IFileStorage</c>.
/// A relative <see cref="Root"/> resolves under the host content root (the API project folder).
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Storage root. Relative paths resolve against <c>IHostEnvironment.ContentRootPath</c>.</summary>
    public string Root { get; init; } = "Media";

    /// <summary>Subfolder under <see cref="Root"/> that holds uploads not yet claimed by a submission.</summary>
    public string PendingFolder { get; init; } = "pending";

    /// <summary>Subfolder under <see cref="Root"/> that submitted form media is moved into.</summary>
    public string SubmissionsFolder { get; init; } = "forms";

    /// <summary>Largest accepted upload, in megabytes.</summary>
    public int MaxFileSizeMb { get; init; } = 25;

    /// <summary>Accepted content types. Empty means "no restriction"; entries may end with <c>/*</c>.</summary>
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = [];
}
