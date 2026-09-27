using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace NWFM.Shared.Persistence;

/// <summary>
/// Moves a module's EF migration history table into the module's new schema before migrations run.
/// </summary>
/// <remarks>
/// A module keeps its history inside its own schema, so renaming the schema would leave EF looking
/// for history in the new one, finding none, and replaying every migration from the first — onto
/// tables that already exist. Carrying the history table across first lets EF see what was applied
/// and run only the migration that moves the module's tables. Idempotent: once the table is in the
/// new schema, or on a database created after the rename, this does nothing.
/// </remarks>
public static partial class MigrationHistorySchemaMove
{
    /// <param name="executeSql">
    /// Runs raw SQL against the context — the module's <c>Database.ExecuteSqlRawAsync</c>. Passed in so
    /// this project need not take a dependency on the relational provider.
    /// </param>
    public static async Task RunAsync(
        DbContext context,
        string previousSchema,
        string schema,
        string historyTable,
        Func<string, CancellationToken, Task<int>> executeSql,
        CancellationToken cancellationToken)
    {
        foreach (var identifier in new[] { previousSchema, schema, historyTable })
        {
            // Spliced into DDL below, so only plain identifiers are accepted.
            if (!IdentifierRegex().IsMatch(identifier))
            {
                throw new ArgumentException($"'{identifier}' is not a plain SQL identifier.");
            }
        }

        // A database that does not exist yet has no history to move; MigrateAsync will create it.
        if (!await context.Database.CanConnectAsync(cancellationToken))
        {
            return;
        }

        var sql = $"""
            IF OBJECT_ID(N'[{previousSchema}].[{historyTable}]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{schema}].[{historyTable}]', N'U') IS NULL
            BEGIN
                IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA [{schema}]');
                EXEC(N'ALTER SCHEMA [{schema}] TRANSFER [{previousSchema}].[{historyTable}]');
            END
            """;

        await executeSql(sql, cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
