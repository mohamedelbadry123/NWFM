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
    /// <summary>
    /// The most answer columns one form's table may hold, across all its versions. SQL Server caps a
    /// table at 1,024 columns and an in-row record at 8,060 bytes; fixed-width columns (a DECIMAL is
    /// 13 bytes) count in full against the second, so the practical ceiling sits well below the first.
    /// </summary>
    public const int MaxStoredFields = 500;

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

        var candidates = FieldCandidates(schema).ToList();

        await using var transaction = await context.BeginTransactionIfNoneAsync(cancellationToken);

        try
        {
            // Two publishes of this form at once must not both pass the type check and then race to
            // alter its table. Publishes of other forms touch other tables and do not wait.
            await submissionStore.AcquireLockAsync(FormStorageLocks.Form(form.Id), cancellationToken);

            var existing = await LoadFieldsAsync(form.Id, cancellationToken);

            // Every refusal is found before anything is changed, so a refused publish leaves the
            // change tracker exactly as it found it.
            foreach (var candidate in candidates)
            {
                if (existing.TryGetValue(candidate.DataName, out var known)
                    && !string.Equals(known.FieldType, candidate.FieldType, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure<FormDefinition>(
                        FormEngineErrors.Field.TypeConflict(candidate.DataName, known.FieldType, candidate.FieldType));
                }
            }

            // Columns are only ever added, so the table holds every field any version has declared.
            var storedCount = existing.Count + candidates.Count(c => !existing.ContainsKey(c.DataName));
            if (storedCount > MaxStoredFields)
            {
                return Result.Failure<FormDefinition>(FormEngineErrors.Schema.TooManyFields(storedCount, MaxStoredFields));
            }

            var tableName = form.SubmissionTable ?? await AllocateTableNameAsync(form.Code, cancellationToken);

            var utcNow = timeProvider.GetUtcNow().UtcDateTime;

            var snapshots = new[]
            {
                new FormVersionSnapshot(FormTargetClients.Formly, form.SchemaJson, BuildSnapshotJson(form)),
            };

            form.Publish(publishedBy, snapshots, utcNow);
            form.AssignSubmissionTable(tableName);

            Register(form.Id, form.CurrentVersionNo!.Value, candidates, existing, utcNow);

            await context.SaveChangesAsync(cancellationToken);

            // Inside the same transaction: SQL Server DDL is transactional, so a column that fails to
            // add rolls the version back with it instead of leaving a version nobody can submit to.
            await submissionStore.EnsureFormTableAsync(TableOf(form.Id, tableName, existing.Values), cancellationToken);

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

    public async Task<Result> EnsureStorageAsync(Guid formDefinitionId, CancellationToken cancellationToken)
    {
        var form = await context.FormDefinitions
            .FirstOrDefaultAsync(x => x.Id == formDefinitionId, cancellationToken);

        if (form is null)
        {
            return Result.Failure(FormEngineErrors.Form.NotFound);
        }

        if (!form.HasPublishedVersion)
        {
            return Result.Success();
        }

        var versions = await context.FormVersions
            .AsNoTracking()
            .Where(v => v.FormDefinitionId == form.Id && v.TargetClient == FormTargetClients.Formly)
            .OrderBy(v => v.VersionNo)
            .Select(v => new { v.VersionNo, v.SchemaJson })
            .ToListAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionIfNoneAsync(cancellationToken);

        try
        {
            await submissionStore.AcquireLockAsync(FormStorageLocks.Form(form.Id), cancellationToken);

            var existing = await LoadFieldsAsync(form.Id, cancellationToken);
            var utcNow = timeProvider.GetUtcNow().UtcDateTime;

            // Oldest first, so a field's column is typed by the version that introduced it — which is
            // what the values already written under that version were.
            foreach (var version in versions)
            {
                var candidates = FieldCandidates(FormSchemaParser.Parse(version.SchemaJson))
                    .Where(c => FormDataName.IsValid(c.DataName) && !FormSubmissionColumns.IsBase(c.DataName))
                    .Where(c => !existing.TryGetValue(c.DataName, out var known)
                        || string.Equals(known.FieldType, c.FieldType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Register(form.Id, version.VersionNo, candidates, existing, utcNow);
            }

            var tableName = form.SubmissionTable ?? await AllocateTableNameAsync(form.Code, cancellationToken);
            form.AssignSubmissionTable(tableName);

            await context.SaveChangesAsync(cancellationToken);

            await submissionStore.EnsureFormTableAsync(TableOf(form.Id, tableName, existing.Values), cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(FormEngineErrors.Form.ConcurrencyConflict);
        }
        catch (DomainException ex)
        {
            return Result.Failure(FormEngineErrors.Form.Invalid(ex.Message));
        }

        await cache.RemoveAsync(CacheKeys.FormEngine.FieldCatalog, cancellationToken);

        return Result.Success();
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

    private async Task<Dictionary<string, FormField>> LoadFieldsAsync(Guid formDefinitionId, CancellationToken cancellationToken)
    {
        var fields = await context.FormFields
            .Where(f => f.FormDefinitionId == formDefinitionId)
            .ToListAsync(cancellationToken);

        return fields.ToDictionary(f => f.DataName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records <paramref name="candidates"/> as declared by <paramref name="versionNo"/>: a known field
    /// is marked as still in use, a new one is added. <paramref name="known"/> is updated in place, so
    /// it describes the whole table afterwards.
    /// </summary>
    private void Register(
        Guid formDefinitionId,
        int versionNo,
        IEnumerable<FieldCandidate> candidates,
        Dictionary<string, FormField> known,
        DateTime utcNow)
    {
        foreach (var candidate in candidates)
        {
            if (known.TryGetValue(candidate.DataName, out var field))
            {
                field.SeenIn(versionNo, candidate.LabelEn, candidate.LabelAr, utcNow);
                continue;
            }

            field = FormField.Create(
                formDefinitionId,
                candidate.DataName,
                candidate.FieldType,
                candidate.LabelEn,
                candidate.LabelAr,
                candidate.IsCompanion,
                versionNo,
                utcNow);

            context.FormFields.Add(field);
            known[field.DataName] = field;
        }
    }

    /// <summary>
    /// The first name for the form's code that no other form holds. Serialised across every form,
    /// because two codes can collapse to one name (<c>A-B</c> and <c>A_B</c>) and would otherwise both
    /// claim it and fail on the unique index.
    /// </summary>
    private async Task<string> AllocateTableNameAsync(string formCode, CancellationToken cancellationToken)
    {
        await submissionStore.AcquireLockAsync(FormStorageLocks.TableNaming, cancellationToken);

        var baseName = FormSubmissionTableName.For(formCode);

        var taken = await context.FormDefinitions
            .AsNoTracking()
            .Where(f => f.SubmissionTable != null && f.SubmissionTable.StartsWith(baseName))
            .Select(f => f.SubmissionTable!)
            .ToListAsync(cancellationToken);

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        return FormSubmissionTableName.Candidates(formCode).FirstOrDefault(name => !used.Contains(name))
            ?? throw new DomainException($"No free submission table name is left for form code '{formCode}'.");
    }

    private static FormTable TableOf(Guid formDefinitionId, string tableName, IEnumerable<FormField> fields) =>
        FormTable.Create(
            formDefinitionId,
            tableName,
            fields.Select(f => new KeyValuePair<string, string>(f.DataName, f.FieldType)));

    /// <summary>
    /// The columns this schema needs: its own fields, plus a <c>&lt;data_name&gt;_other</c> companion for
    /// each choice field offering "Other". The companion carries the typed free text, so it is a real
    /// column and is registered — and type-checked — like any other.
    /// </summary>
    private static IEnumerable<FieldCandidate> FieldCandidates(FormSchema schema)
    {
        foreach (var field in schema.Fields)
        {
            yield return new FieldCandidate(field.DataName, field.FieldType, field.LabelEn, field.LabelAr, IsCompanion: false);

            if (FormChoiceOther.NeedsCompanion(field))
            {
                yield return new FieldCandidate(
                    FormChoiceOther.KeyFor(field.DataName),
                    FormElementTypes.Text,
                    Suffixed(field.LabelEn, OtherLabelSuffixEn),
                    Suffixed(field.LabelAr, OtherLabelSuffixAr),
                    IsCompanion: true);
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

    private sealed record FieldCandidate(
        string DataName,
        string FieldType,
        string? LabelEn,
        string? LabelAr,
        bool IsCompanion);
}
