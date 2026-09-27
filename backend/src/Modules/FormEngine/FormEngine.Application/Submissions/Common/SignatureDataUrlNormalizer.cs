using System.Text.Json;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Uploads.Common;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using NWFM.Shared.Options;
using NWFM.Shared.Storage;

namespace FormEngine.Application.Submissions.Common;

/// <summary>
/// Turns a signature answer that is still a <c>data:image/...;base64,...</c> string into a pending
/// <see cref="SubmissionFile"/> and replaces the answer with the same file-reference array every
/// other media field uses. The web client uploads the PNG first and sends the array itself; this
/// keeps a client that posts the raw data URL working.
/// </summary>
internal static class SignatureDataUrlNormalizer
{
    private const string DefaultContentType = "image/png";
    private const string DataUrlPrefix = "data:image/";
    private const string DataPrefix = "data:";
    private const string Base64Marker = ";base64,";
    private const string FileIdProperty = "fileId";
    private const string SignatureFileStem = "signature";

    /// <summary>
    /// Mutates <paramref name="answers"/> in place. Persists any new pending file before returning so
    /// a following <see cref="SubmissionMediaLinker.LinkAsync"/> can claim it.
    /// </summary>
    public static async Task NormalizeAsync(
        IFormEngineDbContext context,
        IFileStorage fileStorage,
        FileStorageOptions settings,
        FormSchema schema,
        Guid formDefinitionId,
        int? versionNo,
        string? contextType,
        string? contextId,
        string? uploadedBy,
        IDictionary<string, object?> answers,
        CancellationToken cancellationToken)
    {
        var wroteFiles = false;

        foreach (var field in schema.Fields)
        {
            if (!string.Equals(field.FieldType, FormElementTypes.Signature, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!answers.TryGetValue(field.DataName, out var value) || value is null)
            {
                continue;
            }

            if (IsFileRefArray(value))
            {
                continue;
            }

            if (!TryGetDataUrlText(value, out var dataUrl))
            {
                // Truncated labels (`data:image/png;base64,… (11.3 KB)`) and other junk — drop them
                // so the column is not left with a non-image string.
                if (IsTruncatedOrNonImage(value))
                {
                    answers[field.DataName] = null;
                }

                continue;
            }

            if (!TryDecodeDataUrl(dataUrl, out var contentType, out var bytes))
            {
                answers[field.DataName] = null;
                continue;
            }

            var maxBytes = (long)settings.MaxFileSizeMb * FormFileRules.BytesPerMb;
            if (bytes.Length == 0 || bytes.Length > maxBytes)
            {
                answers[field.DataName] = null;
                continue;
            }

            if (!FormFileRules.IsAllowedContentType(contentType, settings.AllowedContentTypes))
            {
                answers[field.DataName] = null;
                continue;
            }

            var fileId = Guid.NewGuid();
            var extension = ExtensionFor(contentType);
            var storedName = fileId.ToString("N") + extension;
            var displayName = SignatureFileStem + extension;

            await using var stream = new MemoryStream(bytes, writable: false);
            var stored = await fileStorage.SaveAsync(
                stream,
                storedName,
                contentType,
                settings.PendingFolder,
                cancellationToken);

            context.SubmissionFiles.Add(SubmissionFile.CreatePending(
                fileId,
                formDefinitionId,
                versionNo,
                field.DataName,
                displayName,
                contentType,
                stored.SizeBytes,
                stored.RelativePath,
                contextType,
                contextId,
                uploadedBy));

            answers[field.DataName] = BuildFileRefJson(
                fileId,
                stored.RelativePath,
                displayName,
                contentType,
                stored.SizeBytes);

            wroteFiles = true;
        }

        if (wroteFiles)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsFileRefArray(object value) => value switch
    {
        JsonElement json when json.ValueKind == JsonValueKind.Array => HasFileId(json),
        string text when text.TrimStart().StartsWith('[') => LooksLikeFileRefJson(text),
        IEnumerable<object> => true,
        _ => false,
    };

    private static bool HasFileId(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(FileIdProperty, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeFileRefJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Array && HasFileId(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDataUrlText(object value, out string dataUrl)
    {
        dataUrl = value switch
        {
            string text => text,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(dataUrl)
            || !dataUrl.StartsWith(DataUrlPrefix, StringComparison.OrdinalIgnoreCase)
            || dataUrl.Contains('…')
            || !dataUrl.Contains(Base64Marker, StringComparison.OrdinalIgnoreCase))
        {
            dataUrl = string.Empty;
            return false;
        }

        return true;
    }

    private static bool IsTruncatedOrNonImage(object value)
    {
        var text = value switch
        {
            string s => s,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? string.Empty,
            _ => string.Empty,
        };

        return text.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodeDataUrl(string dataUrl, out string contentType, out byte[] bytes)
    {
        contentType = DefaultContentType;
        bytes = [];

        var comma = dataUrl.IndexOf(',');
        if (comma <= 0 || comma >= dataUrl.Length - 1)
        {
            return false;
        }

        var header = dataUrl[..comma];
        var payload = dataUrl[(comma + 1)..];

        var mimeStart = DataPrefix.Length;
        var mimeEnd = header.IndexOf(';', mimeStart);
        if (mimeEnd > mimeStart)
        {
            contentType = header[mimeStart..mimeEnd].Trim();
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = DefaultContentType;
            }
        }

        if (!header.Contains(Base64Marker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(payload);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string BuildFileRefJson(Guid fileId, string path, string name, string type, long size) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                fileId,
                path,
                name,
                type,
                size,
            },
        });

    private static string ExtensionFor(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
                ? ".webp"
                : ".png";
}
