using FormEngine.Application.Common.Schema;

namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// Native-SQL data access for form submissions. Every form writes to the one shared
/// <c>FE.Submissions</c> table — fixed base columns plus one nullable column per <c>data_name</c>,
/// separated by <c>FormDefinitionId</c> — so submissions can be queried across forms without a
/// union. A column's SQL type is taken from the canonical <c>FE.FieldCatalog</c> entry, so a
/// <c>data_name</c> can never end up with two different types. All statements are loaded from an
/// editable SQL JSON file. Intentionally outside the EF model (no DbSet / entity / migration).
/// Every statement joins the transaction open on the FormEngine context, if any.
/// </summary>
public interface IFormSubmissionStore
{
    /// <summary>Creates the shared table, its base columns and its indexes if any are missing.</summary>
    Task EnsureTableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serialises schema changes to the shared table across concurrent publishes. Must run inside an
    /// open transaction; the lock is released when that transaction ends.
    /// </summary>
    Task AcquireSchemaLockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the shared table exists and adds any column <paramref name="schema"/>'s fields still
    /// need. Only ever adds or widens — never narrows or drops, so historical values stay readable.
    /// </summary>
    Task ReconcileTableAsync(FormSchema schema, CancellationToken cancellationToken);

    /// <summary>Inserts one submission (answers keyed by <c>data_name</c>); returns the new row id.</summary>
    Task<Guid> InsertAsync(FormSubmissionInsert submission, CancellationToken cancellationToken);

    /// <summary>
    /// Overwrites the named answer columns on an existing row. Only the keys given — and only those
    /// <paramref name="schema"/> declares — are touched; base columns are never written.
    /// </summary>
    Task UpdateAnswersAsync(
        Guid formDefinitionId,
        Guid submissionId,
        FormSchema schema,
        IReadOnlyDictionary<string, object?> answers,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a single submission row as a column-keyed dictionary, or null if not found. Projects the
    /// base columns plus every field column any published version of the form declares.
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        Guid formDefinitionId,
        Guid submissionId,
        CancellationToken cancellationToken);

    /// <summary>The most recent submission a context (e.g. one work item) recorded, or null when it has none.</summary>
    Task<IReadOnlyDictionary<string, object?>?> GetLatestByContextAsync(
        Guid formDefinitionId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken);

    /// <summary>Server-side paged list of a form's submission rows, newest first.</summary>
    Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Items, int Total)> ListAsync(
        FormSubmissionListFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// The id of the submission already recorded under this client key, or null when it is new. A
    /// client that loses its connection mid-send retries with the same key; this is how a retry is
    /// told apart from a second fill.
    /// </summary>
    Task<Guid?> FindByClientIdAsync(Guid formDefinitionId, Guid clientSubmissionId, CancellationToken cancellationToken);
}

/// <summary>One row to write to the shared <c>FE.Submissions</c> table.</summary>
public sealed record FormSubmissionInsert
{
    public required Guid FormDefinitionId { get; init; }

    public required int VersionNo { get; init; }

    /// <summary>The schema of <see cref="VersionNo"/> — what decides which answer keys have a column.</summary>
    public required FormSchema Schema { get; init; }

    /// <summary>What owns the fill, e.g. <c>WorkItem</c>. Set together with <see cref="ContextId"/>.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    public string? SubmittedBy { get; init; }

    public string? SubmittedByName { get; init; }

    /// <summary>
    /// When the fill was made. Null — the normal case — stamps the row with the server clock. Set only
    /// by an importer of historical data, so the row keeps the date the work was actually done.
    /// </summary>
    public DateTimeOffset? SubmittedDate { get; init; }

    /// <summary>The client's own key for the fill; uniquely indexed per form so a retry cannot write twice.</summary>
    public Guid? ClientSubmissionId { get; init; }

    /// <summary>Answers keyed by field <c>data_name</c>. Keys the schema does not declare are ignored.</summary>
    public required IReadOnlyDictionary<string, object?> Answers { get; init; }
}

/// <summary>Which of a form's submissions to page through.</summary>
public sealed record FormSubmissionListFilter
{
    public required Guid FormDefinitionId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? ContextType { get; init; }
    public string? ContextId { get; init; }
}
