using FormEngine.Domain.Entities;
using NWFM.Shared.Results;

namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// Freezes a form's working schema into a new version. Shared by the publish endpoint and the
/// database seeder, so both register catalog entries and submission columns the same way.
/// </summary>
public interface IFormPublisher
{
    /// <summary>
    /// Validates the schema's data names, upserts the field catalog, creates the version and adds any
    /// missing <c>FE.Submissions</c> columns — all in one transaction, so a type conflict or a failed
    /// column change leaves nothing half-published.
    /// </summary>
    Task<Result<FormDefinition>> PublishAsync(Guid formDefinitionId, string? publishedBy, CancellationToken cancellationToken);
}
