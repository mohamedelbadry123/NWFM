using FormEngine.Domain.Constants;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace FormEngine.Domain.Entities;

/// <summary>
/// An immutable copy of a form's schema, frozen at publish time. One row per target client per
/// publish. Submissions are written and validated against the version they name, never the
/// working draft. Table: <c>FormEngine.FormVersions</c>.
/// </summary>
public sealed class FormVersion : Entity, IImmutableEntity
{
    public const int TargetClientMaxLength = 20;

    private FormVersion()
    {
    }

    private FormVersion(
        Guid formDefinitionId,
        int versionNo,
        string targetClient,
        string schemaJson,
        string snapshotJson,
        string? publishedBy,
        DateTime publishedAt)
    {
        FormDefinitionId = formDefinitionId;
        VersionNo = versionNo;
        TargetClient = targetClient;
        SchemaJson = schemaJson;
        SnapshotJson = snapshotJson;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
        CreatedAt = publishedAt;
        UpdatedAt = publishedAt;
    }

    public Guid FormDefinitionId { get; private set; }
    public int VersionNo { get; private set; }
    public string TargetClient { get; private set; } = default!;

    /// <summary>The form-builder document exactly as it was when this version was published.</summary>
    public string SchemaJson { get; private set; } = default!;

    /// <summary>The form's code, names and category at publish time (<c>{code,nameEn,nameAr,category}</c>).</summary>
    public string SnapshotJson { get; private set; } = default!;

    public string? PublishedBy { get; private set; }
    public DateTime PublishedAt { get; private set; }

    internal static FormVersion Create(
        Guid formDefinitionId,
        int versionNo,
        string targetClient,
        string schemaJson,
        string snapshotJson,
        string? publishedBy,
        DateTime publishedAt)
    {
        if (versionNo <= 0)
        {
            throw new DomainException("A form version number must be positive.");
        }

        if (!FormTargetClients.IsDefined(targetClient))
        {
            throw new DomainException($"Unknown target client '{targetClient}'.");
        }

        return new FormVersion(
            formDefinitionId,
            versionNo,
            targetClient,
            string.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson,
            string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson,
            publishedBy,
            publishedAt);
    }
}
