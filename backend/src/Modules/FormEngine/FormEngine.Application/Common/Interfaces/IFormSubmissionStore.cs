using FormEngine.Application.Common.Schema;

namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// Native-SQL data access for form submissions. Each published form writes to its own table in the
/// <c>FE</c> schema — fixed base columns plus one typed column per <c>data_name</c> — so no form's
/// fields cost another form a column, and publishing one form never alters a table another is
/// writing to. Every method takes the <see cref="FormTable"/> it works on. All statements are loaded
/// from an editable SQL JSON file, and every one joins the transaction open on the FormEngine
/// context, if any.
/// </summary>
public interface IFormSubmissionStore
{
    /// <summary>
    /// Takes an exclusive application lock on <paramref name="resource"/> (see
    /// <see cref="FormStorageLocks"/>). Must run inside an open transaction; the lock is released
    /// when that transaction ends.
    /// </summary>
    Task AcquireLockAsync(string resource, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the form's table if it is missing, then adds any column its fields still need. Only
    /// ever adds or widens — never narrows or drops, so values written under an older version stay
    /// readable.
    /// </summary>
    Task EnsureFormTableAsync(FormTable table, CancellationToken cancellationToken);

    /// <summary>Inserts one submission (answers keyed by <c>data_name</c>); returns the new row id.</summary>
    Task<Guid> InsertAsync(FormTable table, FormSubmissionInsert submission, CancellationToken cancellationToken);

    /// <summary>One submission row as a column-keyed dictionary, or null if there is none.</summary>
    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        FormTable table,
        Guid submissionId,
        CancellationToken cancellationToken);

    /// <summary>The most recent submission a context (e.g. one task) recorded, or null when it has none.</summary>
    Task<IReadOnlyDictionary<string, object?>?> GetLatestByContextAsync(
        FormTable table,
        string contextType,
        string contextId,
        CancellationToken cancellationToken);

    /// <summary>Server-side paged list of the form's submission rows, newest first.</summary>
    Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Items, int Total)> ListAsync(
        FormTable table,
        FormSubmissionListFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// The id of the submission already recorded under this client key, or null when it is new. A
    /// client that loses its connection mid-send retries with the same key; this is how a retry is
    /// told apart from a second fill.
    /// </summary>
    Task<Guid?> FindByClientIdAsync(FormTable table, Guid clientSubmissionId, CancellationToken cancellationToken);
}

/// <summary>One row to write to a form's submission table.</summary>
public sealed record FormSubmissionInsert
{
    public required int VersionNo { get; init; }

    /// <summary>What owns the fill, e.g. <c>Task</c>. Set together with <see cref="ContextId"/>.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    public string? SubmittedBy { get; init; }

    public string? SubmittedByName { get; init; }

    /// <summary>
    /// When the fill was made. Null — the normal case — stamps the row with the server clock. Set only
    /// by an importer of historical data, so the row keeps the date the work was actually done.
    /// </summary>
    public DateTimeOffset? SubmittedDate { get; init; }

    /// <summary>The client's own key for the fill; uniquely indexed so a retry cannot write twice.</summary>
    public Guid? ClientSubmissionId { get; init; }

    /// <summary>Answers keyed by field <c>data_name</c>. Keys the table has no column for are ignored.</summary>
    public required IReadOnlyDictionary<string, object?> Answers { get; init; }
}

/// <summary>Which of a form's submissions to page through.</summary>
public sealed record FormSubmissionListFilter
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? ContextType { get; init; }
    public string? ContextId { get; init; }
}
