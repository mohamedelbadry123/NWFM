using FormEngine.Domain.Constants;
using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace FormEngine.Domain.Entities;

/// <summary>
/// One media file uploaded against a form field. Created the moment the user picks the file —
/// before the form is submitted — so the browser never carries bytes into the submit payload; the
/// submission row only stores a reference array of file id + <see cref="RelativePath"/>.
/// The row <see cref="Entity.Id"/> is the public file handle. Table: <c>FE.SubmissionFiles</c>.
/// </summary>
public sealed class SubmissionFile : Entity
{
    public const int DataNameMaxLength = 200;
    public const int FileNameMaxLength = 400;
    public const int ContentTypeMaxLength = 200;
    public const int RelativePathMaxLength = 1000;
    public const int StatusMaxLength = 20;
    public const int ActorMaxLength = 256;

    private const string DefaultContentType = "application/octet-stream";

    private SubmissionFile()
    {
    }

    private SubmissionFile(
        Guid fileId,
        Guid formDefinitionId,
        int? formVersionNo,
        string dataName,
        string fileName,
        string contentType,
        long sizeBytes,
        string relativePath,
        string? contextType,
        string? contextId,
        string? uploadedBy,
        string storageKind)
    {
        Id = fileId;
        FormDefinitionId = formDefinitionId;
        FormVersionNo = formVersionNo;
        DataName = dataName;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        RelativePath = relativePath;
        ContextType = contextType;
        ContextId = contextId;
        UploadedBy = uploadedBy;
        StorageKind = storageKind;
        Status = SubmissionFileStatuses.Pending;
        IsActive = true;
    }

    public Guid FormDefinitionId { get; private set; }

    /// <summary>The version the file was picked against, when the client named one.</summary>
    public int? FormVersionNo { get; private set; }

    /// <summary>Null while the file is <c>PENDING</c>; set when the form is submitted.</summary>
    public Guid? SubmissionId { get; private set; }

    /// <summary>What the fill belongs to (e.g. <c>WorkItem</c>), when known at upload or submit time.</summary>
    public string? ContextType { get; private set; }

    public string? ContextId { get; private set; }

    /// <summary>The field (<c>data_name</c>) the file was picked for.</summary>
    public string DataName { get; private set; } = default!;

    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    /// <summary>Storage-root-relative path — resolved through <c>IFileStorage</c>, never used directly.</summary>
    public string RelativePath { get; private set; } = default!;

    /// <summary>See <see cref="SubmissionFileStatuses"/>.</summary>
    public string Status { get; private set; } = default!;

    /// <summary>Whether this application owns the bytes — see <see cref="SubmissionFileStorageKinds"/>.</summary>
    public string StorageKind { get; private set; } = default!;

    /// <summary>User id of whoever uploaded the file; only they may read or remove it while pending.</summary>
    public string? UploadedBy { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsPending => Status == SubmissionFileStatuses.Pending;

    /// <summary>
    /// True when the bytes belong to an imported archive rather than to this application. Callers
    /// that would move or delete the file must check this first — reading never needs to.
    /// </summary>
    public bool IsMigrated => StorageKind == SubmissionFileStorageKinds.Migrated;

    public static SubmissionFile CreatePending(
        Guid fileId,
        Guid formDefinitionId,
        int? formVersionNo,
        string dataName,
        string fileName,
        string contentType,
        long sizeBytes,
        string relativePath,
        string? contextType,
        string? contextId,
        string? uploadedBy) =>
        Create(
            fileId,
            formDefinitionId,
            formVersionNo,
            dataName,
            fileName,
            contentType,
            sizeBytes,
            relativePath,
            contextType,
            contextId,
            uploadedBy,
            SubmissionFileStorageKinds.Managed);

    /// <summary>
    /// References a file already sitting on the server rather than one this application wrote — e.g.
    /// the media of an imported Fulcrum export. Identical to <see cref="CreatePending"/> for readers;
    /// the row only disclaims ownership of the bytes, so nothing later moves or deletes them.
    /// </summary>
    public static SubmissionFile CreateMigrated(
        Guid fileId,
        Guid formDefinitionId,
        int? formVersionNo,
        string dataName,
        string fileName,
        string contentType,
        long sizeBytes,
        string relativePath,
        string? contextType,
        string? contextId,
        string? uploadedBy) =>
        Create(
            fileId,
            formDefinitionId,
            formVersionNo,
            dataName,
            fileName,
            contentType,
            sizeBytes,
            relativePath,
            contextType,
            contextId,
            uploadedBy,
            SubmissionFileStorageKinds.Migrated);

    private static SubmissionFile Create(
        Guid fileId,
        Guid formDefinitionId,
        int? formVersionNo,
        string dataName,
        string fileName,
        string contentType,
        long sizeBytes,
        string relativePath,
        string? contextType,
        string? contextId,
        string? uploadedBy,
        string storageKind)
    {
        if (fileId == Guid.Empty)
        {
            throw new DomainException("An uploaded file must have a file id.");
        }

        if (formDefinitionId == Guid.Empty)
        {
            throw new DomainException("An uploaded file must belong to a form.");
        }

        if (string.IsNullOrWhiteSpace(dataName))
        {
            throw new DomainException("An uploaded file must name the field it belongs to.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainException("An uploaded file must have a file name.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new DomainException("An uploaded file must have a storage path.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException("An uploaded file must not be empty.");
        }

        if (!SubmissionFileStorageKinds.IsDefined(storageKind))
        {
            throw new DomainException($"Unknown file storage kind '{storageKind}'.");
        }

        var name = fileName.Trim();
        if (name.Length > FileNameMaxLength)
        {
            name = name[^FileNameMaxLength..];
        }

        return new SubmissionFile(
            fileId,
            formDefinitionId,
            formVersionNo,
            dataName.Trim(),
            name,
            string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType.Trim(),
            sizeBytes,
            relativePath.Trim(),
            NormalizeOptional(contextType),
            NormalizeOptional(contextId),
            NormalizeOptional(uploadedBy),
            storageKind);
    }

    /// <summary>
    /// Ties a still-pending file to the submission that referenced it, and backfills the context it
    /// now belongs to. A context already on the row is kept as it is.
    /// </summary>
    public void LinkTo(Guid submissionId, string? newRelativePath, string? contextType, string? contextId, DateTime utcNow)
    {
        if (submissionId == Guid.Empty)
        {
            throw new DomainException("A submission id is required to link an uploaded file.");
        }

        if (Status != SubmissionFileStatuses.Pending)
        {
            throw new DomainException($"File '{Id}' is already linked to submission {SubmissionId}.");
        }

        SubmissionId = submissionId;
        Status = SubmissionFileStatuses.Linked;

        if (ContextType is null && !string.IsNullOrWhiteSpace(contextType) && !string.IsNullOrWhiteSpace(contextId))
        {
            ContextType = contextType.Trim();
            ContextId = contextId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(newRelativePath))
        {
            RelativePath = newRelativePath;
        }

        SetUpdated(utcNow);
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        SetUpdated(utcNow);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
