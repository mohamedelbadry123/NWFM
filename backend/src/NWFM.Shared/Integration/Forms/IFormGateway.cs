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

    /// <summary>
    /// The answerable fields of one published version, in the form's order, with what an integration
    /// needs of them: each field's C2M parameter name, and each choice's C2M close outcome. Empty when
    /// the version is unknown.
    /// </summary>
    Task<IReadOnlyList<FormFieldInfo>> GetFieldsAsync(Guid formId, int versionNo, CancellationToken cancellationToken);

    /// <summary>
    /// A fill's answers as a person reads them, in the order the answered version lays its fields
    /// out. Each carries the question's labels and the answer rendered in both languages, so a
    /// choice reads as its option's label and a media answer as its file names. Blank answers are
    /// left out. Empty when the version is unknown.
    /// </summary>
    Task<IReadOnlyList<FormAnswerView>> DescribeAnswersAsync(
        Guid formId,
        int versionNo,
        IReadOnlyDictionary<string, object?> answers,
        CancellationToken cancellationToken);

    /// <summary>
    /// The bytes of one file a context's fills claimed — for embedding in a report. Null when the
    /// file is not the context's, has been removed, is larger than <paramref name="maxBytes"/>, or
    /// its bytes are gone from storage.
    /// </summary>
    Task<byte[]?> ReadContextFileAsync(
        Guid fileId,
        string contextType,
        string contextId,
        long maxBytes,
        CancellationToken cancellationToken);
}

/// <summary>
/// One answer ready to show. <see cref="DisplayEn"/>/<see cref="DisplayAr"/> are the answer in each
/// language; <see cref="Point"/> is set for a geolocation answer, so a client can put it on a map.
/// </summary>
public sealed record FormAnswerView(
    string DataName,
    string FieldType,
    string? LabelEn,
    string? LabelAr,
    string DisplayEn,
    string DisplayAr,
    FormAnswerPoint? Point);

public sealed record FormAnswerPoint(double Latitude, double Longitude, string? Address);

/// <summary>
/// One answerable field of a published version. <see cref="C2mParameterName"/> is the name its answer
/// travels under when a field activity is closed in C2M; null when it is not sent.
/// </summary>
public sealed record FormFieldInfo(
    string DataName,
    string FieldType,
    string? LabelEn,
    string? LabelAr,
    string? C2mParameterName,
    IReadOnlyList<FormChoiceInfo> Choices);

/// <summary>
/// One option of a choice field. <see cref="C2mFaStatus"/> (<c>C</c> or <c>X</c>) and
/// <see cref="C2mReason"/> are set on the options of the Action Taken field that decide how a field
/// activity closes in C2M.
/// </summary>
public sealed record FormChoiceInfo(
    string Value,
    string? LabelEn,
    string? LabelAr,
    string? C2mFaStatus,
    string? C2mReason);

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
