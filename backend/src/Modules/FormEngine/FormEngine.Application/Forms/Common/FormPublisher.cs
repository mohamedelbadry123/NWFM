using System.Text.Json;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Constants;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Common;

/// <inheritdoc />
public sealed class FormPublisher(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore,
    ICacheService cache,
    TimeProvider timeProvider) : IFormPublisher
{
    /// <summary>Appended to a companion's labels so the catalog list reads as what it is.</summary>
    private const string OtherLabelSuffixEn = " (other)";

    private const string OtherLabelSuffixAr = " (أخرى)";

    private const string PublishedAction = "published";

    public async Task<Result<FormDefinition>> PublishAsync(
        Guid formDefinitionId,
        string? publishedBy,
        CancellationToken cancellationToken)
    {
        var form = await context.FormDefinitions
            .FirstOrDefaultAsync(x => x.Id == formDefinitionId, cancellationToken);

        if (form is null)
        {
            return Result.Failure<FormDefinition>(FormEngineErrors.Form.NotFound);
        }

        if (FormStatuses.IsFrozen(form.Status))
        {
            return Result.Failure<FormDefinition>(
                FormEngineErrors.Form.InvalidStatusTransition(form.Status, PublishedAction));
        }

        if (!FormSchemaParser.IsValidJson(form.SchemaJson))
        {
            return Result.Failure<FormDefinition>(FormEngineErrors.Schema.InvalidJson);
        }

        var schema = FormSchemaParser.Parse(form.SchemaJson);

        // Sections alone carry no value, so a schema of empty sections has nothing to publish either.
        if (schema.Fields.Count == 0)
        {
            return Result.Failure<FormDefinition>(FormEngineErrors.Schema.Empty);
        }

        // Every data_name becomes a SQL column. Caught here rather than at submit time, where an
        // unwritable answer is simply dropped and the fill looks successful with a NULL behind it.
        if (ValidateDataNames(schema) is { } nameError)
        {
            return Result.Failure<FormDefinition>(nameError);
        }

        var catalogEntries = CatalogEntries(schema).ToList();

        await using var transaction = await context.BeginTransactionIfNoneAsync(cancellationToken);

        try
        {
            // Serialises publishes: two forms introducing the same data_name at once must not both
            // pass the catalog check and then race to create the column.
            await submissionStore.AcquireSchemaLockAsync(cancellationToken);

            var existing = await LoadCatalogAsync(catalogEntries, cancellationToken);

            // Every conflict is found before anything is added, so a refused publish leaves the
            // change tracker exactly as it found it.
            foreach (var entry in catalogEntries)
            {
                if (existing.TryGetValue(entry.DataName, out var known)
                    && !string.Equals(known.FieldType, entry.FieldType, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure<FormDefinition>(
                        FormEngineErrors.FieldCatalog.TypeConflict(entry.DataName, known.FieldType, entry.FieldType));
                }
            }

            foreach (var entry in catalogEntries.Where(entry => !existing.ContainsKey(entry.DataName)))
            {
                context.FieldCatalog.Add(FieldCatalogEntry.Create(entry.DataName, entry.FieldType, entry.LabelEn, entry.LabelAr));
            }

            var snapshots = new[]
            {
                new FormVersionSnapshot(FormTargetClients.Formly, form.SchemaJson, BuildSnapshotJson(form)),
            };

            form.Publish(publishedBy, snapshots, timeProvider.GetUtcNow().UtcDateTime);

            await context.SaveChangesAsync(cancellationToken);

            // Inside the same transaction: SQL Server DDL is transactional, so a column that fails to
            // add rolls the version back with it instead of leaving a version nobody can submit to.
            await submissionStore.ReconcileTableAsync(schema, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<FormDefinition>(FormEngineErrors.Form.ConcurrencyConflict);
        }
        catch (DomainException ex)
        {
            return Result.Failure<FormDefinition>(FormEngineErrors.Form.Invalid(ex.Message));
        }

        await cache.RemoveAsync(CacheKeys.FormEngine.FieldCatalog, cancellationToken);

        return Result.Success(form);
    }

    /// <summary>
    /// Rejects names that could not become a column: not a legal identifier, used twice in the one
    /// schema (two fields would share a column and overwrite each other), or taken by a base column.
    /// The parser trims first, so <c>"leak_type "</c> passes as the <c>leak_type</c> it becomes.
    /// </summary>
    private static Error? ValidateDataNames(FormSchema schema)
    {
        var invalid = schema.Fields
            .Select(f => f.DataName)
            .Where(name => !FormDataName.IsValid(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalid.Count > 0)
        {
            return FormEngineErrors.Schema.InvalidDataName(invalid);
        }

        var names = FormWritableFields.Of(schema).Select(f => f.Name).ToList();

        var reserved = names
            .Where(FormSubmissionColumns.IsBase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reserved.Count > 0)
        {
            return FormEngineErrors.Schema.ReservedDataName(reserved);
        }

        var duplicates = names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        return duplicates.Count > 0 ? FormEngineErrors.Schema.DuplicateDataName(duplicates) : null;
    }

    private async Task<Dictionary<string, FieldCatalogEntry>> LoadCatalogAsync(
        IReadOnlyCollection<CatalogCandidate> entries,
        CancellationToken cancellationToken)
    {
        var names = entries.Select(e => e.DataName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var existing = await context.FieldCatalog
            .Where(c => names.Contains(c.DataName))
            .ToListAsync(cancellationToken);

        return existing.ToDictionary(c => c.DataName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The names this form registers: its own fields, plus a <c>&lt;data_name&gt;_other</c> companion for
    /// each choice field offering "Other". The companion is a real column carrying the typed free text,
    /// so it belongs in the catalog like any other name — which is also what makes the type-conflict
    /// check cover it.
    /// </summary>
    private static IEnumerable<CatalogCandidate> CatalogEntries(FormSchema schema)
    {
        foreach (var field in schema.Fields)
        {
            yield return new CatalogCandidate(field.DataName, field.FieldType, field.LabelEn, field.LabelAr);

            if (FormChoiceOther.NeedsCompanion(field))
            {
                yield return new CatalogCandidate(
                    FormChoiceOther.KeyFor(field.DataName),
                    FormElementTypes.Text,
                    Suffixed(field.LabelEn, OtherLabelSuffixEn),
                    Suffixed(field.LabelAr, OtherLabelSuffixAr));
            }
        }
    }

    private static string? Suffixed(string? label, string suffix) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Trim() + suffix;

    private static string BuildSnapshotJson(FormDefinition form) =>
        JsonSerializer.Serialize(new
        {
            code = form.Code,
            nameEn = form.NameEn,
            nameAr = form.NameAr,
            category = form.Category,
        });

    private sealed record CatalogCandidate(string DataName, string FieldType, string? LabelEn, string? LabelAr);
}
