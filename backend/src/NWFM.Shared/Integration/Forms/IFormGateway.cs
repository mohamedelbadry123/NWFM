using NWFM.Shared.Results;

namespace NWFM.Shared.Integration.Forms;

/// <summary>
/// What another module may ask of the form engine. Implemented by FormEngine; consumed — Tasks, a
/// workflow task screen — without referencing it. A consumer pins a form by id and version number
/// and hands fills back through <see cref="SubmitAsync"/>, which runs the same validation, replay and
/// media handling as the form engine's own submit endpoint.
/// </summary>
public interface IFormGateway
{
    /// <summary>A form that has a published version, or null when there is none.</summary>
    Task<PublishedFormInfo?> FindPublishedAsync(Guid formId, CancellationToken cancellationToken);

    /// <summary>The same, looked up by code — used by seeders that know a form by name.</summary>
    Task<PublishedFormInfo?> FindPublishedByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Forms that can take submissions, for a picker. Newest-named first is not implied; ordered by code.</summary>
    Task<IReadOnlyList<PublishedFormInfo>> ListPublishedAsync(string? search, int take, CancellationToken cancellationToken);

    /// <summary>The frozen form-builder document of one published version, or null when there is no such version.</summary>
    Task<string?> GetVersionSchemaAsync(Guid formId, int versionNo, CancellationToken cancellationToken);

    /// <summary>
    /// Records one fill against a pinned version. A second send under the same client key answers with
    /// the first submission (<see cref="FormSubmitReceipt.IsReplay"/>) instead of writing twice.
    /// Failures carry the form engine's own error codes.
    /// </summary>
    Task<Result<FormSubmitReceipt>> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken);

    /// <summary>The newest fill a context recorded against a form, or null when it has none.</summary>
    Task<FormSubmissionRecord?> GetLatestByContextAsync(
        Guid formId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken);

    /// <summary>Every fill a context recorded against a form, newest first.</summary>
    Task<IReadOnlyList<FormSubmissionRecord>> ListByContextAsync(
        Guid formId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken);

    /// <summary>Media claimed by the context's fills, newest first.</summary>
    Task<IReadOnlyList<FormFileRecord>> ListFilesByContextAsync(
        string contextType,
        string contextId,
        CancellationToken cancellationToken);
}

/// <summary>A form that can be filled, at its current published version.</summary>
public sealed record PublishedFormInfo(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string Category,
    string Status,
    int CurrentVersionNo,
    bool AcceptsSubmissions);

/// <summary>One fill to record.</summary>
public sealed record FormSubmitRequest
{
    public required Guid FormId { get; init; }

    /// <summary>The pinned version. Always sent by a consumer, so a redesign never changes the form under work in flight.</summary>
    public required int VersionNo { get; init; }

    /// <summary>What owns the fill — e.g. <c>Task</c> — and its id. Both or neither.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    public Guid? ClientSubmissionId { get; init; }

    public DateTimeOffset? ClientFilledAt { get; init; }

    /// <summary>Answers keyed by field <c>data_name</c>.</summary>
    public required IReadOnlyDictionary<string, object?> Answers { get; init; }
}

/// <summary>A recorded fill: new, or the one a replayed client key already wrote.</summary>
public sealed record FormSubmitReceipt(Guid SubmissionId, int VersionNo, bool IsReplay);

/// <summary>
/// One stored fill. <see cref="Answers"/> holds the stored values keyed by <c>data_name</c>, as the
/// columns return them — a yes/no answer is a boolean, a date a <see cref="DateTime"/>, a media or
/// multi-choice answer JSON text — which is what the web client's renderer reads back.
/// </summary>
public sealed record FormSubmissionRecord(
    Guid SubmissionId,
    Guid FormId,
    int VersionNo,
    string? SubmittedBy,
    string? SubmittedByName,
    DateTimeOffset? SubmittedDate,
    IReadOnlyDictionary<string, object?> Answers);

/// <summary>A media file a fill carries.</summary>
public sealed record FormFileRecord(
    Guid FileId,
    Guid FormId,
    Guid? SubmissionId,
    string DataName,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    DateTime CreatedAt);
