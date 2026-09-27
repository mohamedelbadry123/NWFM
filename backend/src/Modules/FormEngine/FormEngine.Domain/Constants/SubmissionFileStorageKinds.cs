namespace FormEngine.Domain.Constants;

/// <summary>
/// Who owns the bytes behind a <c>SubmissionFile</c> row.
///
/// <see cref="Managed"/> files were written by this application — an upload or a signature — so it is
/// free to move them when a submission claims them and to delete them when the row goes.
/// <see cref="Migrated"/> files came from a historical archive (e.g. a Fulcrum export) placed on the
/// server in bulk and only referenced: copying it through the app would hold two of it, and a single
/// delete could destroy the master copy. Reading is identical for both.
/// </summary>
public static class SubmissionFileStorageKinds
{
    /// <summary>Written by this application; movable and deletable.</summary>
    public const string Managed = "MANAGED";

    /// <summary>From an imported archive, referenced in place; never moved, never deleted.</summary>
    public const string Migrated = "MIGRATED";

    public static readonly IReadOnlyList<string> All =
    [
        Managed,
        Migrated
    ];

    public static bool IsDefined(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
