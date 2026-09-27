using System.Text.Json;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Submissions.Common;

/// <summary>
/// Claims the media files a submission referenced, and files them under the submission they now
/// belong to.
/// </summary>
internal static class SubmissionMediaLinker
{
    /// <summary>
    /// Links the files this submission's answers name. Only files uploaded against the same form and
    /// still <c>PENDING</c> can be claimed, so a fabricated file id cannot steal a file belonging to
    /// another submission. Left unsaved — the caller commits once.
    /// </summary>
    public static async Task LinkAsync(
        IFormEngineDbContext context,
        IFileStorage fileStorage,
        FormSchema schema,
        IReadOnlyDictionary<string, object?> answers,
        Guid formDefinitionId,
        Guid submissionId,
        string destinationFolder,
        string? contextType,
        string? contextId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var fileIds = MediaFileReferences.Extract(schema, answers);
        if (fileIds.Count == 0)
        {
            return;
        }

        var files = await context.SubmissionFiles
            .Where(f => fileIds.Contains(f.Id)
                && f.FormDefinitionId == formDefinitionId
                && f.Status == SubmissionFileStatuses.Pending)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            // A migrated file belongs to an imported archive, referenced where it already sits.
            // Passing no new path leaves it exactly where it is; LinkTo ignores an empty one.
            var newPath = file.IsMigrated
                ? string.Empty
                : await fileStorage.MoveAsync(file.RelativePath, destinationFolder, file.FileName, cancellationToken);

            file.LinkTo(submissionId, newPath, contextType, contextId, utcNow);
        }
    }
}

/// <summary>
/// Pulls the <c>fileId</c>s out of the media answers. A media field's value is a JSON array of
/// <c>{ fileId, path, name, type, size }</c> references — the bytes live in storage, never here.
/// </summary>
internal static class MediaFileReferences
{
    private const string FileIdProperty = "fileId";

    public static IReadOnlyCollection<Guid> Extract(FormSchema schema, IReadOnlyDictionary<string, object?> answers)
    {
        var mediaNames = schema.Fields
            .Where(f => FormElementTypes.IsMedia(f.FieldType))
            .Select(f => f.DataName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (mediaNames.Count == 0)
        {
            return [];
        }

        var fileIds = new HashSet<Guid>();

        foreach (var (key, value) in answers)
        {
            // `mediaNames` comes from the parsed schema, whose data names are trimmed; the caller has
            // already put the answers on those same keys (see FormAnswerKeys).
            if (!mediaNames.Contains(key))
            {
                continue;
            }

            CollectFrom(value, fileIds);
        }

        return fileIds;
    }

    private static void CollectFrom(object? value, HashSet<Guid> sink)
    {
        switch (value)
        {
            case JsonElement json:
                CollectFromJson(json, sink);
                return;

            // Media answers (including signature) are a JSON array of file refs. A legacy data-URL
            // string is normalised before linking runs, so it never reaches here.
            case string text when text.TrimStart().StartsWith('['):
                try
                {
                    using var document = JsonDocument.Parse(text);
                    CollectFromJson(document.RootElement, sink);
                }
                catch (JsonException)
                {
                    // Not a reference array; nothing to link.
                }

                return;
        }
    }

    private static void CollectFromJson(JsonElement element, HashSet<Guid> sink)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(FileIdProperty, out var fileId)
                || fileId.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (Guid.TryParse(fileId.GetString(), out var parsed))
            {
                sink.Add(parsed);
            }
        }
    }
}
