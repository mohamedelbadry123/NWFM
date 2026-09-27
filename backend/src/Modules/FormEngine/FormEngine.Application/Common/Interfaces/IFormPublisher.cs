using FormEngine.Domain.Entities;
using NWFM.Shared.Results;

namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// Freezes a form's working schema into a new version. Shared by the publish endpoint and the
/// database seeder, so both register the form's fields and create its submission table the same way.
/// </summary>
public interface IFormPublisher
{
    /// <summary>
    /// Validates the schema's data names, records the form's fields, creates the version and creates
    /// or extends the form's own submission table — all in one transaction, so a type conflict or a
    /// failed column change leaves nothing half-published.
    /// </summary>
    Task<Result<FormDefinition>> PublishAsync(Guid formDefinitionId, string? publishedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Makes sure a published form has its table, its field registry and every column its versions
    /// declare, rebuilding whatever is missing from the published versions themselves. Repairs a form
    /// published before it had a table of its own, and a database restored without the native tables.
    /// A form with no published version has nothing to repair and succeeds untouched.
    /// </summary>
    Task<Result> EnsureStorageAsync(Guid formDefinitionId, CancellationToken cancellationToken);
}
