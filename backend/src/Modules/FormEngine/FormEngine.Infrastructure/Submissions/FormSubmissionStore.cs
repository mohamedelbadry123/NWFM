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
/// Native-SQL form submission store. Each published form writes to its own table in the <c>FE</c>
/// schema: fixed base columns plus one typed column per <c>data_name</c>. A column's SQL type comes
/// from the form's own <c>FE.FormFields</c> registry, handed in on the <see cref="FormTable"/>.
/// Table names are checked against the closed <c>SUB_[A-Z0-9_]+</c> alphabet and column names against
/// the data-name rule before either reaches SQL; every value flows through a parameter.
/// </summary>
public sealed partial class FormSubmissionStore(FormEngineDbContext context, SqlStatementStore sql) : IFormSubmissionStore
{
    private static readonly IReadOnlyList<string> BaseColumns = FormSubmissionColumns.All;

    /// <summary>The indexes every submission table carries: name pattern, and the statement that creates it.</summary>
    private static readonly (string NamePattern, string Statement)[] Indexes =
    [
        ("IX_{0}_SubmittedDate", "CreateSubmittedDateIndex"),
        ("IX_{0}_Context", "CreateContextIndex"),
        ("UX_{0}_ClientSubmissionId", "CreateClientSubmissionIndex"),
    ];

    /// <summary>SQL Server's duplicate-key errors: a unique index and a unique constraint.</summary>
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task AcquireLockAsync(string resource, CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken);
        try
        {
            using var command = CreateCommand();
            command.CommandText = sql.Get("AcquireLock");
            AddParameter(command, "@resource", resource);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var status = result is null or DBNull ? -1 : Convert.ToInt32(result, CultureInfo.InvariantCulture);

            // 0 and 1 mean granted; anything negative is a timeout or a deadlock victim.
            if (status < 0)
            {
                throw new InvalidOperationException(
                    $"Timed out waiting for the '{resource}' lock. Another publish is in progress.");
            }
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task EnsureFormTableAsync(FormTable table, CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(target, cancellationToken))
            {
                await ExecuteAsync(Statement("CreateTable", target), cancellationToken);
            }

            // One catalog read rather than one per column: a form can carry hundreds of fields.
            var existing = await LoadPhysicalColumnsAsync(target, cancellationToken);

            foreach (var (column, fieldType) in table.FieldTypes)
            {
                var sqlType = SqlTypeFor(fieldType);

                if (existing.Contains(column))
                {
                    // A field type's column can grow between builds — geolocation gained an address,
                    // so a 100-character column no longer holds the answer. Widening here is what
                    // keeps a table created by an older build writable.
                    await WidenIfNarrowerAsync(target, column, sqlType, cancellationToken);
                    continue;
                }

                await ExecuteAsync(
                    Statement("AddColumn", target).Replace("{column}", Quote(column)).Replace("{type}", sqlType),
                    cancellationToken);
            }

            // After the columns, so every index is created over columns that exist by then.
            var indexes = await LoadIndexNamesAsync(target, cancellationToken);

            foreach (var (namePattern, statement) in Indexes)
            {
                if (!indexes.Contains(string.Format(CultureInfo.InvariantCulture, namePattern, target.Bare)))
                {
                    await ExecuteAsync(Statement(statement, target), cancellationToken);
                }
            }
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<Guid> InsertAsync(FormTable table, FormSubmissionInsert submission, CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        // Types, not just names: a value has to be handed to ADO as the CLR type its column was
        // created with, since the client speaks the form builder's vocabulary ('yes' for a yes/no
        // field) while the column is a BIT.
        var accepted = Accept(submission.Answers, table.FieldTypes);

        var columnsBuilder = new StringBuilder();
        var paramsBuilder = new StringBuilder();

        await OpenAsync(cancellationToken);
        try
        {
            using var command = CreateCommand();

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
                AddParameter(command, paramName, CoerceForColumn(table.FieldTypes[key], key, value));
                index++;
            }

            command.CommandText = Statement("Insert", target)
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

    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        FormTable table,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(target, cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = Statement("GetById", target).Replace("{select}", SelectList(table));
            AddParameter(command, "@submissionId", submissionId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
        }
        finally
        {
            await CloseAsync();
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetLatestByContextAsync(
        FormTable table,
        string contextType,
        string contextId,
        CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(target, cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = Statement("GetLatestByContext", target).Replace("{select}", SelectList(table));
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
        FormTable table,
        FormSubmissionListFilter filter,
        CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        await OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(target, cancellationToken))
            {
                return ([], 0);
            }

            int total;
            using (var countCommand = CreateCommand())
            {
                countCommand.CommandText = Statement("Count", target);
                AddParameter(countCommand, "@contextType", filter.ContextType);
                AddParameter(countCommand, "@contextId", filter.ContextId);
                total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            var items = new List<IReadOnlyDictionary<string, object?>>();
            using (var listCommand = CreateCommand())
            {
                listCommand.CommandText = Statement("List", target).Replace("{select}", SelectList(table));
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
        FormTable table,
        Guid clientSubmissionId,
        CancellationToken cancellationToken)
    {
        var target = Target.Of(table);

        await OpenAsync(cancellationToken);
        try
        {
            // A form that has never taken a submission may have no table yet, which is a "not seen
            // before" answer rather than a failure.
            if (!await TableExistsAsync(target, cancellationToken))
            {
                return null;
            }

            using var command = CreateCommand();
            command.CommandText = Statement("GetIdByClientId", target);
            AddParameter(command, "@clientSubmissionId", clientSubmissionId);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is Guid id ? id : null;
        }
        finally
        {
            await CloseAsync();
        }
    }

    /// <summary>
    /// The answers that may be written: known to the table, a legal identifier, and each column only
    /// once. Case-insensitive, because SQL Server column names are — two keys differing only in case
    /// would otherwise produce the same column twice in one statement.
    /// </summary>
    private static List<(string Key, object? Value)> Accept(
        IReadOnlyDictionary<string, object?> answers,
        IReadOnlyDictionary<string, string> fieldTypes)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return answers
            // Trimmed to match the column even though the submit path normalises keys first: this is
            // a public store, and a caller that skipped that step would otherwise have its answers
            // dropped in silence rather than written.
            .Select(kvp => (Key: kvp.Key.Trim(), kvp.Value))
            .Where(kvp => fieldTypes.ContainsKey(kvp.Key) && IsValidIdentifier(kvp.Key) && written.Add(kvp.Key))
            .ToList();
    }

    /// <summary>
    /// Base columns plus the form's registered answer columns. The registry is written in the same
    /// transaction that creates the columns, so it lists exactly what the table holds — no need to ask
    /// SQL Server on every read.
    /// </summary>
    private static string SelectList(FormTable table)
    {
        var selected = new List<string>(BaseColumns.Count + table.FieldTypes.Count);
        selected.AddRange(BaseColumns.Select(Quote));
        selected.AddRange(table.FieldTypes.Keys
            .Where(column => !FormSubmissionColumns.IsBase(column) && IsValidIdentifier(column))
            .Select(Quote));

        return string.Join(", ", selected);
    }

    private async Task<HashSet<string>> LoadPhysicalColumnsAsync(Target target, CancellationToken cancellationToken) =>
        await ReadNamesAsync("ColumnNames", target, cancellationToken);

    private async Task<HashSet<string>> LoadIndexNamesAsync(Target target, CancellationToken cancellationToken) =>
        await ReadNamesAsync("IndexNames", target, cancellationToken);

    private async Task<HashSet<string>> ReadNamesAsync(string statement, Target target, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = CreateCommand();
        command.CommandText = sql.Get(statement);
        AddParameter(command, "@tableName", target.ObjectName);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<bool> TableExistsAsync(Target target, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("TableExists");
        AddParameter(command, "@tableName", target.ObjectName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task ExecuteAsync(string commandText, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A statement with its table placeholders filled in for <paramref name="target"/>.</summary>
    private string Statement(string key, Target target) =>
        sql.Get(key).Replace("{table}", target.Qualified).Replace("{name}", target.Bare);

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
    /// row, so this can run on each publish without a guard of its own.
    /// </summary>
    private async Task WidenIfNarrowerAsync(Target target, string column, string sqlType, CancellationToken cancellationToken)
    {
        if (!await IsNarrowerAsync(target, column, sqlType, cancellationToken))
        {
            return;
        }

        await ExecuteAsync(
            Statement("AlterColumn", target).Replace("{column}", Quote(column)).Replace("{type}", sqlType),
            cancellationToken);
    }

    private async Task<bool> IsNarrowerAsync(Target target, string column, string sqlType, CancellationToken cancellationToken)
    {
        var nvarchar = NVarCharLengthRegex().Match(sqlType);
        if (nvarchar.Success)
        {
            var wanted = int.Parse(nvarchar.Groups[1].Value, CultureInfo.InvariantCulture);
            var current = await ColumnMaxLengthAsync(target, column, cancellationToken);

            // -1 is NVARCHAR(MAX) — already wider than any fixed length. Bytes, so two per character.
            return current is not (null or MaxLengthSentinel) && current / 2 < wanted;
        }

        var decimalType = DecimalPrecisionRegex().Match(sqlType);
        if (decimalType.Success)
        {
            var wantedPrecision = int.Parse(decimalType.Groups[1].Value, CultureInfo.InvariantCulture);
            var wantedScale = int.Parse(decimalType.Groups[2].Value, CultureInfo.InvariantCulture);
            var current = await ColumnDecimalPrecisionAsync(target, column, cancellationToken);

            // A different scale is a different number, not a wider one — left alone.
            return current is (var precision, var scale) && scale == wantedScale && precision < wantedPrecision;
        }

        return false;
    }

    /// <summary>What <c>sys.columns.max_length</c> reports for an unbounded column.</summary>
    private const int MaxLengthSentinel = -1;

    private async Task<int?> ColumnMaxLengthAsync(Target target, string columnName, CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnMaxLength");
        AddParameter(command, "@tableName", target.ObjectName);
        AddParameter(command, "@columnName", columnName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Precision and scale of a <c>DECIMAL</c> column, or null when it is not one.</summary>
    private async Task<(int Precision, int Scale)?> ColumnDecimalPrecisionAsync(
        Target target,
        string columnName,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand();
        command.CommandText = sql.Get("ColumnDecimalPrecision");
        AddParameter(command, "@tableName", target.ObjectName);
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

    /// <summary>
    /// A form's table in the three shapes SQL needs it: bracket-quoted for statements, bare for index
    /// and constraint names, and schema-dotted for <c>OBJECT_ID</c>. The name is re-checked here, the
    /// last point before it reaches SQL, so a value edited in the database cannot become an injection.
    /// </summary>
    private readonly record struct Target(string Qualified, string Bare, string ObjectName)
    {
        public static Target Of(FormTable table)
        {
            if (!FormSubmissionTableName.IsValid(table.TableName))
            {
                throw new InvalidOperationException($"Rejected unsafe submission table name '{table.TableName}'.");
            }

            return new Target(
                $"[{FormEngineSchema.Name}].[{table.TableName}]",
                table.TableName,
                $"{FormEngineSchema.Name}.{table.TableName}");
        }
    }
}
