using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Domain.Constants;
using FormEngine.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NWFM.Shared.Exceptions;

namespace FormEngine.Infrastructure.Submissions;

/// <summary>
/// Native-SQL form submission store. Every form writes to the one shared <c>FE.Submissions</c> table:
/// fixed base columns plus one nullable column per <c>data_name</c>, separated by
/// <c>FormDefinitionId</c>. A column's SQL type comes from the canonical <c>FE.FieldCatalog</c> entry,
/// so a <c>data_name</c> can never hold two different types. Identifiers are whitelisted against the
/// form's own field set and wrapped with <c>[]</c>; all values flow through parameters.
/// </summary>
public sealed partial class FormSubmissionStore(FormEngineDbContext context, SqlStatementStore sql) : IFormSubmissionStore
{
    /// <summary>What <c>OBJECT_ID</c> is asked about.</summary>
    private static readonly string TableName = $"{FormEngineSchema.Name}.{FormEngineSchema.Submissions}";

    private static readonly IReadOnlyList<string> BaseColumns = FormSubmissionColumns.All;

    /// <summary>
    /// Base columns that a table created by an older build may lack. Reconciliation adds them the
    /// same way it adds a form's field columns — nullable, never altering existing rows.
    /// </summary>
    private static readonly (string Column, string SqlType)[] AddableBaseColumns =
    [
        (FormSubmissionColumns.ContextType, "NVARCHAR(100)"),
        (FormSubmissionColumns.ContextId, "NVARCHAR(100)"),
        (FormSubmissionColumns.SubmittedByName, "NVARCHAR(256)"),
        (FormSubmissionColumns.ClientSubmissionId, "UNIQUEIDENTIFIER"),
    ];

    /// <summary>Indexes the table carries, and the statement that creates each.</summary>
    private static readonly (string Name, string Statement)[] Indexes =
    [
        ("IX_FE_Submissions_Form_SubmittedDate", "CreateFormIndex"),
        ("IX_FE_Submissions_Context", "CreateContextIndex"),
        ("UX_FE_Submissions_ClientSubmissionId", "CreateClientSubmissionIndex"),
    ];

    /// <summary>SQL Server's duplicate-key errors: a unique index and a unique constraint.</summary>
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken);
        try
        {
            await EnsureTableCoreAsync(cancellationToken);
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task AcquireSchemaLockAsync(CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken);
        try
        {
            using var command = CreateCommand();
            command.CommandText = sql.Get("AcquireSchemaLock");

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var status = result is null or DBNull ? -1 : Convert.ToInt32(result, CultureInfo.InvariantCulture);

            // 0 and 1 mean granted; anything negative is a timeout or a deadlock victim.
            if (status < 0)
            {
                throw new InvalidOperationException(
                    "Timed out waiting for the submission table lock. Another publish is in progress.");
            }
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task ReconcileTableAsync(FormSchema schema, CancellationToken cancellationToken)
    {
        // Resolved before the connection is opened — this runs an EF query of its own.
        var columns = await MapColumnsAsync(schema, cancellationToken);

        await OpenAsync(cancellationToken);
        try
        {
            await EnsureTableCoreAsync(cancellationToken);

            foreach (var (column, type) in columns)
            {
                if (await ColumnExistsAsync(column, cancellationToken))
                {
                    // A field type's column can grow between builds — geolocation gained an address,
                    // so a 100-character column no longer holds the answer. Widening here is what
                    // keeps a table created by an older build writable.
                    await WidenIfNarrowerAsync(column, type, cancellationToken);
                    continue;
                }

                await ExecuteAsync(
                    sql.Get("AddColumn").Replace("{column}", Quote(column)).Replace("{type}", type),
                    cancellationToken);
            }
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<Guid> InsertAsync(FormSubmissionInsert submission, CancellationToken cancellationToken)
    {
        // Types, not just names: a value has to be handed to ADO as the CLR type its column was
        // created with, since the client speaks the form builder's vocabulary ('yes' for a yes/no
        // field) while the column is a BIT.
        var fieldTypes = await ResolveFieldTypesAsync(submission.Schema, cancellationToken);
        var accepted = Accept(submission.Answers, fieldTypes);

        var columnsBuilder = new StringBuilder();
        var paramsBuilder = new StringBuilder();

        await OpenAsync(cancellationToken);
        try
        {
            using var command = CreateCommand();

            AddParameter(command, "@formDefinitionId", submission.FormDefinitionId);
            AddParameter(command, "@versionNo", submission.VersionNo);
            AddParameter(command, "@status", FormSubmissionStatuses.Submitted);
            AddParameter(command, "@contextType", submission.ContextType);
            AddParameter(command, "@contextId", submission.ContextId);
            AddParameter(command, "@submittedBy", submission.SubmittedBy);
            AddParameter(command, "@submittedByName", submission.SubmittedByName);
            // The caller's own fill time wins when it has one — an importer of historical records
            // knows when the work was done, and the clock now would erase that.
            AddParameter(command, "@submittedDate", submission.SubmittedDate ?? DateTimeOffset.UtcNow);
            AddParameter(command, "@clientSubmissionId", submission.ClientSubmissionId);

            var index = 0;
            foreach (var (key, value) in accepted)
            {
                var paramName = "@p" + index.ToString(CultureInfo.InvariantCulture);
                columnsBuilder.Append(", ").Append(Quote(key));
                paramsBuilder.Append(", ").Append(paramName);
                AddParameter(command, paramName, CoerceForColumn(fieldTypes[key], key, value));
                index++;
            }

            command.CommandText = sql.Get("Insert")
                .Replace("{columns}", columnsBuilder.ToString())
                .Replace("{params}", paramsBuilder.ToString());

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is Guid id ? id : Guid.Parse(Convert.ToString(result, CultureInfo.InvariantCulture)!);
        }
        catch (SqlException ex) when (submission.ClientSubmissionId is not null && IsDuplicateKey(ex))
        {
            // Two retries of the same queued fill arrived at once; the unique index caught the loser.
            throw new DuplicateClientSubmissionException(submission.ClientSubmissionId.Value);
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task UpdateAnswersAsync(
        Guid formDefinitionId,
        Guid submissionId,
        FormSchema schema,
        IReadOnlyDictionary<string, object?> answers,
        CancellationToken cancellationToken)
    {
        var fieldTypes = await ResolveFieldTypesAsync(schema, cancellationToken);
        var accepted = Accept(answers, fieldTypes);

        // No accepted key means no SET clause, and `UPDATE ... SET WHERE` is a syntax error. Nothing
        // to write is not a failure, so return rather than build an invalid statement.
        if (accepted.Count == 0)
        {
            return;
        }

        var assignments = new StringBuilder();

        await OpenAsync(cancellationToken);
        try
        {
            using var command = CreateCommand();

            AddParameter(command, "@submissionId", submissionId);
            AddParameter(command, "@formDefinitionId", formDefinitionId);

            var index = 0;
            foreach (var (key, value) in accepted)
            {
                var paramName = "@p" + index.ToString(CultureInfo.InvariantCulture);

                if (index > 0)
                {
                    assignments.Append(", ");
                }

                assignments.Append(Quote(key)).Append(" = ").Append(paramName);
                AddParameter(command, paramName, CoerceForColumn(fieldTypes[key], key, value));
                index++;
            }

            command.CommandText = sql.Get("UpdateAnswers").Replace("{assignments}", assignments.ToString());

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        Guid formDefinitionId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var allowed = await LoadColumnWhitelistAsync(formDefinitionId, cancellationToken);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = sql.Get("GetById")
                .Replace("{select}", await BuildSelectListAsync(allowed, cancellationToken));
            AddParameter(command, "@submissionId", submissionId);
            AddParameter(command, "@formDefinitionId", formDefinitionId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetLatestByContextAsync(
        Guid formDefinitionId,
        string contextType,
        string contextId,
        CancellationToken cancellationToken)
    {
        var allowed = await LoadColumnWhitelistAsync(formDefinitionId, cancellationToken);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = sql.Get("GetLatestByContext")
                .Replace("{select}", await BuildSelectListAsync(allowed, cancellationToken));
            AddParameter(command, "@formDefinitionId", formDefinitionId);
            AddParameter(command, "@contextType", contextType);
            AddParameter(command, "@contextId", contextId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Items, int Total)> ListAsync(
        FormSubmissionListFilter filter,
        CancellationToken cancellationToken)
    {
        var allowed = await LoadColumnWhitelistAsync(filter.FormDefinitionId, cancellationToken);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(cancellationToken))
            {
                return ([], 0);
            }

            int total;
            using (var countCommand = CreateCommand())
            {
                countCommand.CommandText = sql.Get("CountByForm");
                AddParameter(countCommand, "@formDefinitionId", filter.FormDefinitionId);
                AddParameter(countCommand, "@contextType", filter.ContextType);
                AddParameter(countCommand, "@contextId", filter.ContextId);
                total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            var selectList = await BuildSelectListAsync(allowed, cancellationToken);

            var items = new List<IReadOnlyDictionary<string, object?>>();
            using (var listCommand = CreateCommand())
            {
                listCommand.CommandText = sql.Get("ListByForm").Replace("{select}", selectList);
                AddParameter(listCommand, "@formDefinitionId", filter.FormDefinitionId);
                AddParameter(listCommand, "@contextType", filter.ContextType);
                AddParameter(listCommand, "@contextId", filter.ContextId);
                AddParameter(listCommand, "@skip", (filter.PageNumber - 1) * filter.PageSize);
                AddParameter(listCommand, "@take", filter.PageSize);

                using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadRow(reader));
                }
            }

            return (items, total);
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<Guid?> FindByClientIdAsync(
        Guid formDefinitionId,
        Guid clientSubmissionId,
        CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken);
        try
        {
            // A database that has never taken a submission has no table to search, which is a
            // "not seen before" answer rather than a failure.
            if (!await TableExistsAsync(cancellationToken)
                || !await ColumnExistsAsync(FormSubmissionColumns.ClientSubmissionId, cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = sql.Get("GetIdByClientId");
            AddParameter(command, "@formDefinitionId", formDefinitionId);
            AddParameter(command, "@clientSubmissionId", clientSubmissionId);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is Guid id ? id : null;
        }
        finally
        {
            await CloseAsync();
        }
    }

    /// <summary>Creates the table, its late-added base columns and its indexes when any are missing.</summary>
    private async Task EnsureTableCoreAsync(CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(cancellationToken))
        {
            await ExecuteAsync(sql.Get("CreateTable"), cancellationToken);
        }

        foreach (var (column, type) in AddableBaseColumns)
        {
            if (!await ColumnExistsAsync(column, cancellationToken))
            {
                await ExecuteAsync(
                    sql.Get("AddColumn").Replace("{column}", Quote(column)).Replace("{type}", type),
                    cancellationToken);
            }
        }

        // After the columns, so every index is created over columns that exist by then.
        foreach (var (name, statement) in Indexes)
        {
            if (!await IndexExistsAsync(name, cancellationToken))
            {
                await ExecuteAsync(sql.Get(statement), cancellationToken);
            }
        }
    }

    /// <summary>
    /// The answers that may be written: known to the schema, a legal identifier, and each column only
    /// once. Case-insensitive, because SQL Server column names are — two keys differing only in case
    /// would otherwise produce the same column twice in one statement.
    /// </summary>
    private static List<(string Key, object? Value)> Accept(
        IReadOnlyDictionary<string, object?> answers,
        IReadOnlyDictionary<string, string> fieldTypes)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return answers
            // Trimmed to match the column even though the submit slice normalises keys first: this is
            // a public store, and a caller that skipped that step would otherwise have its answers
            // dropped in silence rather than written.
            .Select(kvp => (Key: kvp.Key.Trim(), kvp.Value))
            .Where(kvp => fieldTypes.ContainsKey(kvp.Key) && IsValidIdentifier(kvp.Key) && written.Add(kvp.Key))
            .ToList();
    }

    /// <summary>
    /// Base columns plus the form's own <c>data_name</c> columns that physically exist. Projecting
    /// keeps a row payload to this form's fields instead of every column the shared table has
    /// accumulated for other forms.
    /// </summary>
    private async Task<string> BuildSelectListAsync(IReadOnlySet<string> allowed, CancellationToken cancellationToken)
    {
        var existing = await LoadPhysicalColumnsAsync(cancellationToken);

        var selected = new List<string>(BaseColumns.Count + allowed.Count);
        selected.AddRange(BaseColumns.Where(existing.Contains).Select(Quote));
        selected.AddRange(allowed
            .Where(column => existing.Contains(column) && !FormSubmissionColumns.IsBase(column))
            .Select(Quote));

        return selected.Count == 0 ? "*" : string.Join(", ", selected);
    }

    private async Task<HashSet<string>> LoadPhysicalColumnsAsync(CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnNames");
        AddParameter(command, "@tableName", TableName);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    /// Every column any published version of this form can have written. The union matters because a
    /// row written under version 1 may hold a field version 3 has since dropped, and a reader that
    /// only knew the current schema would silently stop showing it.
    /// </summary>
    private async Task<HashSet<string>> LoadColumnWhitelistAsync(Guid formDefinitionId, CancellationToken cancellationToken)
    {
        var schemas = await context.FormVersions
            .AsNoTracking()
            .Where(v => v.FormDefinitionId == formDefinitionId && v.TargetClient == FormTargetClients.Formly)
            .Select(v => v.SchemaJson)
            .ToListAsync(cancellationToken);

        // A form that has never been published can still be read against its working draft.
        if (schemas.Count == 0)
        {
            var draft = await context.FormDefinitions
                .AsNoTracking()
                .Where(f => f.Id == formDefinitionId)
                .Select(f => f.SchemaJson)
                .FirstOrDefaultAsync(cancellationToken);

            if (draft is not null)
            {
                schemas.Add(draft);
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var schemaJson in schemas)
        {
            foreach (var (name, _) in FormWritableFields.Of(FormSchemaParser.Parse(schemaJson)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Resolves each field to the type its column is built on: the canonical <c>FE.FieldCatalog</c>
    /// entry where the <c>data_name</c> is registered, the schema's own type otherwise. Insert and
    /// reconciliation share this, so a value is never coerced to a type the column was not created with.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveFieldTypesAsync(FormSchema schema, CancellationToken cancellationToken)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, fieldType) in FormWritableFields.Of(schema))
        {
            types.TryAdd(name, fieldType);
        }

        if (types.Count == 0)
        {
            return types;
        }

        var names = types.Keys.ToList();

        var canonical = await context.FieldCatalog
            .AsNoTracking()
            .Where(c => names.Contains(c.DataName))
            .Select(c => new { c.DataName, c.FieldType })
            .ToListAsync(cancellationToken);

        foreach (var entry in canonical)
        {
            if (types.ContainsKey(entry.DataName))
            {
                types[entry.DataName] = entry.FieldType;
            }
        }

        return types;
    }

    /// <summary>
    /// Maps the schema's fields to (column, SQL type). The type is taken from the catalog so every
    /// form sharing a <c>data_name</c> shares its column type; the field's own type is only a
    /// fallback for a name not yet in the catalog.
    /// </summary>
    private async Task<IReadOnlyList<(string Column, string SqlType)>> MapColumnsAsync(
        FormSchema schema,
        CancellationToken cancellationToken)
    {
        var fieldTypes = await ResolveFieldTypesAsync(schema, cancellationToken);

        return fieldTypes.Select(field => (field.Key, SqlTypeFor(field.Value))).ToList();
    }

    private async Task<bool> TableExistsAsync(CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("TableExists");
        AddParameter(command, "@tableName", TableName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task<bool> ColumnExistsAsync(string columnName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnExists");
        AddParameter(command, "@tableName", TableName);
        AddParameter(command, "@columnName", columnName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("IndexExists");
        AddParameter(command, "@tableName", TableName);
        AddParameter(command, "@indexName", indexName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task ExecuteAsync(string commandText, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Every statement this store issues goes through here so it joins whatever transaction the caller
    /// has open. ADO commands built straight off the connection do not enlist by themselves, and SQL
    /// Server rejects one issued on a connection with an active transaction it is not part of.
    /// </summary>
    private DbCommand CreateCommand()
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private Task OpenAsync(CancellationToken cancellationToken) =>
        context.Database.OpenConnectionAsync(cancellationToken);

    private Task CloseAsync() => context.Database.CloseConnectionAsync();

    private static bool IsDuplicateKey(SqlException exception) =>
        exception.Errors.Cast<SqlError>().Any(error => DuplicateKeyErrors.Contains(error.Number));

    private static IReadOnlyDictionary<string, object?> ReadRow(DbDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.GetValue(i);
            row[reader.GetName(i)] = value is DBNull ? null : value;
        }

        return row;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Converts JSON-sourced values (JsonElement) to CLR types SQL Server can bind.</summary>
    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => json.TryGetInt64(out var l) ? l : json.GetDouble(),
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Array or JsonValueKind.Object => json.GetRawText(),
                _ => json.ToString(),
            };
        }

        return value;
    }

    /// <summary>
    /// Converts an answer to what its column holds. Values arrive in the form builder's own
    /// vocabulary — a yes/no field stores the string <c>"yes"</c> so its rules can match on it — while
    /// the column is a <c>BIT</c>, so handing the raw value to ADO leaves SQL Server to fail the
    /// conversion with a message that names neither the field nor the answer. Converting here, and
    /// rejecting what cannot convert, is what turns that into an error the caller can act on.
    /// </summary>
    private static object? CoerceForColumn(string fieldType, string dataName, object? value)
    {
        var normalized = NormalizeValue(value);

        if (normalized is null)
        {
            return null;
        }

        return fieldType switch
        {
            FormElementTypes.YesNo => ToBoolean(dataName, normalized),
            FormElementTypes.Numeric => ToNumber(dataName, normalized),
            FormElementTypes.Date => ToDate(dataName, normalized),
            FormElementTypes.Time => ToTime(dataName, normalized),
            FormElementTypes.DateTime => ToDateTime(dataName, normalized),
            FormElementTypes.Geolocation => ToGeolocation(dataName, normalized),
            _ => normalized,
        };
    }

    /// <summary>
    /// Normalises a point to the canonical <c>{"lat":…,"lng":…,"address":…}</c> the column holds.
    /// Clients post it in several shapes — the object, that object's JSON text, a <c>"lat,lng"</c>
    /// pair — and an address of any length; normalising here keeps one shape in the column and stops
    /// an oversized address reaching SQL Server as a truncation error naming nothing.
    /// </summary>
    private static object? ToGeolocation(string dataName, object value)
    {
        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!FormGeolocation.TryRead(value, out var point))
        {
            throw AnswerRejected(dataName, value, AnswerTypeNames.Geolocation);
        }

        return FormGeolocation.ToJson(point);
    }

    private static object? ToBoolean(string dataName, object value) => value switch
    {
        bool flag => flag,
        long number => number != 0,
        double number => number != 0,
        string text => ParseBoolean(dataName, text),
        _ => throw AnswerRejected(dataName, value, AnswerTypeNames.YesNo),
    };

    private static object? ParseBoolean(string dataName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            BooleanTokens.Yes or BooleanTokens.True or BooleanTokens.One => true,
            BooleanTokens.No or BooleanTokens.False or BooleanTokens.Zero => false,
            _ => throw AnswerRejected(dataName, text, AnswerTypeNames.YesNo),
        };
    }

    /// <summary>
    /// Converts to <see cref="decimal"/> and checks the column's range here, not in SQL Server: a
    /// value past it fails there as "arithmetic overflow" naming neither the field nor the value.
    /// </summary>
    private static object? ToNumber(string dataName, object value)
    {
        decimal number;

        switch (value)
        {
            case decimal exact:
                number = exact;
                break;

            case long or int:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                break;

            case double real when double.IsFinite(real) && Math.Abs(real) <= (double)NumericColumnMax:
                number = (decimal)real;
                break;

            case string text when string.IsNullOrWhiteSpace(text):
                return null;

            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                break;

            default:
                throw AnswerRejected(dataName, value, AnswerTypeNames.Number);
        }

        if (Math.Abs(number) > NumericColumnMax)
        {
            throw AnswerRejected(dataName, value, AnswerTypeNames.Number);
        }

        return number;
    }

    private static object? ToDate(string dataName, object value)
    {
        switch (value)
        {
            case DateTime date:
                return date.Date;

            case DateTimeOffset date:
                return date.Date;

            case string text when string.IsNullOrWhiteSpace(text):
                return null;

            case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                return parsed.Date;

            default:
                throw AnswerRejected(dataName, value, AnswerTypeNames.Date);
        }
    }

    /// <summary>
    /// Reads the local <c>YYYY-MM-DD HH:mm</c> the web client writes. The kind is left unspecified
    /// rather than converted: the answer is the wall clock read on site, and shifting it by the
    /// server's offset would store a time nobody recorded.
    /// </summary>
    private static object? ToDateTime(string dataName, object value)
    {
        switch (value)
        {
            case DateTime moment:
                return moment;

            case DateTimeOffset moment:
                return moment.DateTime;

            case string text when string.IsNullOrWhiteSpace(text):
                return null;

            case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                return parsed;

            default:
                throw AnswerRejected(dataName, value, AnswerTypeNames.DateTime);
        }
    }

    private static object? ToTime(string dataName, object value)
    {
        switch (value)
        {
            case TimeSpan time:
                return time;

            case DateTime time:
                return time.TimeOfDay;

            case string text when string.IsNullOrWhiteSpace(text):
                return null;

            case string text when TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed):
                return parsed;

            default:
                throw AnswerRejected(dataName, value, AnswerTypeNames.Time);
        }
    }

    /// <summary>
    /// A rejected answer is the caller's mistake, not a server fault: a <see cref="DomainException"/>
    /// becomes a 400 naming the field rather than a 500 naming nothing.
    /// </summary>
    private static DomainException AnswerRejected(string dataName, object value, string expected) =>
        new($"The answer for '{dataName}' is not a valid {expected} value: '{value}'.");

    /// <summary>What a yes/no answer may arrive as — the builder stores 'yes'/'no'.</summary>
    private static class BooleanTokens
    {
        public const string Yes = "yes";
        public const string No = "no";
        public const string True = "true";
        public const string False = "false";
        public const string One = "1";
        public const string Zero = "0";
    }

    /// <summary>How a rejected answer's expected type reads in the error message.</summary>
    private static class AnswerTypeNames
    {
        public const string YesNo = "yes/no";
        public const string Number = "numeric";
        public const string Date = "date";
        public const string Time = "time";
        public const string DateTime = "date & time";
        public const string Geolocation = "geolocation";
    }

    /// <summary>
    /// Holds the canonical <c>{"lat":…,"lng":…,"address":…}</c> answer. The coordinates take ~50
    /// characters and <see cref="FormGeolocation.MaxAddressLength"/> caps the address, so the value
    /// always fits with room for the JSON envelope and an Arabic address.
    /// </summary>
    private const string GeolocationColumnType = "NVARCHAR(500)";

    /// <summary>
    /// 24 integer and 4 fractional digits: the widest DECIMAL every value of which round-trips through
    /// <see cref="decimal"/>. A 15-digit meter number overflows the more usual DECIMAL(18,4).
    /// </summary>
    private const string NumericColumnType = "DECIMAL(28,4)";

    /// <summary>The largest magnitude <see cref="NumericColumnType"/> holds — keep the two in step.</summary>
    private const decimal NumericColumnMax = 999_999_999_999_999_999_999_999.9999m;

    private static string SqlTypeFor(string fieldType) => fieldType switch
    {
        FormElementTypes.Numeric => NumericColumnType,
        FormElementTypes.YesNo => "BIT",
        FormElementTypes.Date => "DATE",
        FormElementTypes.Time => "TIME",
        // No offset: the answer is the wall clock read on site, not an instant.
        FormElementTypes.DateTime => "DATETIME2(0)",
        FormElementTypes.SingleChoice => "NVARCHAR(400)",
        FormElementTypes.Geolocation => GeolocationColumnType,
        // A decoded code is short — an asset tag or a serial, not a QR carrying a document.
        FormElementTypes.Barcode => "NVARCHAR(400)",
        _ => "NVARCHAR(MAX)",
    };

    /// <summary>
    /// Grows <paramref name="column"/> to <paramref name="sqlType"/> when it is a shorter
    /// <c>NVARCHAR</c>, or a <c>DECIMAL</c> of the same scale but lower precision, than the field now
    /// needs, and does nothing otherwise. Only widening is ever issued: it preserves every existing
    /// row, so this can run on each reconciliation without a guard of its own.
    /// </summary>
    private async Task WidenIfNarrowerAsync(string column, string sqlType, CancellationToken cancellationToken)
    {
        if (!await IsNarrowerAsync(column, sqlType, cancellationToken))
        {
            return;
        }

        await ExecuteAsync(
            sql.Get("AlterColumn").Replace("{column}", Quote(column)).Replace("{type}", sqlType),
            cancellationToken);
    }

    private async Task<bool> IsNarrowerAsync(string column, string sqlType, CancellationToken cancellationToken)
    {
        var nvarchar = NVarCharLengthRegex().Match(sqlType);
        if (nvarchar.Success)
        {
            var wanted = int.Parse(nvarchar.Groups[1].Value, CultureInfo.InvariantCulture);
            var current = await ColumnMaxLengthAsync(column, cancellationToken);

            // -1 is NVARCHAR(MAX) — already wider than any fixed length. Bytes, so two per character.
            return current is not (null or MaxLengthSentinel) && current / 2 < wanted;
        }

        var decimalType = DecimalPrecisionRegex().Match(sqlType);
        if (decimalType.Success)
        {
            var wantedPrecision = int.Parse(decimalType.Groups[1].Value, CultureInfo.InvariantCulture);
            var wantedScale = int.Parse(decimalType.Groups[2].Value, CultureInfo.InvariantCulture);
            var current = await ColumnDecimalPrecisionAsync(column, cancellationToken);

            // A different scale is a different number, not a wider one — left alone.
            return current is (var precision, var scale) && scale == wantedScale && precision < wantedPrecision;
        }

        return false;
    }

    /// <summary>What <c>sys.columns.max_length</c> reports for an unbounded column.</summary>
    private const int MaxLengthSentinel = -1;

    private async Task<int?> ColumnMaxLengthAsync(string columnName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnMaxLength");
        AddParameter(command, "@tableName", TableName);
        AddParameter(command, "@columnName", columnName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Precision and scale of a <c>DECIMAL</c> column, or null when it is not one.</summary>
    private async Task<(int Precision, int Scale)?> ColumnDecimalPrecisionAsync(string columnName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnDecimalPrecision");
        AddParameter(command, "@tableName", TableName);
        AddParameter(command, "@columnName", columnName);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Defers to <see cref="FormDataName"/> so the names publish accepts are exactly the names that can
    /// be written — a second copy of the rule here is how one drifts from the other.
    /// </summary>
    private static bool IsValidIdentifier(string? identifier) => FormDataName.IsValid(identifier);

    private static string Quote(string identifier)
    {
        if (!IsValidIdentifier(identifier))
        {
            throw new InvalidOperationException($"Rejected unsafe SQL identifier '{identifier}'.");
        }

        return "[" + identifier.Replace("]", "]]") + "]";
    }

    /// <summary>Matches a bounded <c>NVARCHAR(n)</c> only — <c>NVARCHAR(MAX)</c> deliberately does not.</summary>
    [GeneratedRegex(@"^NVARCHAR\((\d+)\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NVarCharLengthRegex();

    /// <summary>Matches <c>DECIMAL(p,s)</c>, capturing precision and scale.</summary>
    [GeneratedRegex(@"^DECIMAL\((\d+),(\d+)\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DecimalPrecisionRegex();
}
