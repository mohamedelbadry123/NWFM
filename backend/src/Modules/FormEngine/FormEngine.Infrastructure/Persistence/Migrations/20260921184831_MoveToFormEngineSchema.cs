using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormEngine.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves the module from the <c>FE</c> schema to <c>FormEngine</c>. EF moves the tables it models;
    /// the per-form <c>SUB_*</c> submission tables are outside the model, so they are transferred by
    /// hand, and the emptied <c>FE</c> schema is dropped. The migration history table was already
    /// carried across by the initializer before this ran (see <c>MigrationHistorySchemaMove</c>).
    /// </summary>
    public partial class MoveToFormEngineSchema : Migration
    {
        /// <summary>Every per-form table still in <paramref name="from"/>, transferred to <paramref name="to"/>.</summary>
        private static string TransferSubmissionTables(string from, string to) => $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql = @sql + N'ALTER SCHEMA [{to}] TRANSFER [{from}].' + QUOTENAME(t.name) + N'; '
            FROM sys.tables AS t
            WHERE t.schema_id = SCHEMA_ID(N'{from}') AND t.name LIKE N'SUB[_]%';
            IF LEN(@sql) > 0 EXEC sp_executesql @sql;
            """;

        private static string DropSchemaIfEmpty(string schema) => $"""
            IF SCHEMA_ID(N'{schema}') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'{schema}'))
                EXEC(N'DROP SCHEMA [{schema}]');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "FormEngine");

            migrationBuilder.RenameTable(
                name: "SubmissionFiles",
                schema: "FE",
                newName: "SubmissionFiles",
                newSchema: "FormEngine");

            migrationBuilder.RenameTable(
                name: "FormVersions",
                schema: "FE",
                newName: "FormVersions",
                newSchema: "FormEngine");

            migrationBuilder.RenameTable(
                name: "FormFields",
                schema: "FE",
                newName: "FormFields",
                newSchema: "FormEngine");

            migrationBuilder.RenameTable(
                name: "FormDefinitions",
                schema: "FE",
                newName: "FormDefinitions",
                newSchema: "FormEngine");

            migrationBuilder.Sql(TransferSubmissionTables("FE", "FormEngine"));
            migrationBuilder.Sql(DropSchemaIfEmpty("FE"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "FE");

            migrationBuilder.RenameTable(
                name: "SubmissionFiles",
                schema: "FormEngine",
                newName: "SubmissionFiles",
                newSchema: "FE");

            migrationBuilder.RenameTable(
                name: "FormVersions",
                schema: "FormEngine",
                newName: "FormVersions",
                newSchema: "FE");

            migrationBuilder.RenameTable(
                name: "FormFields",
                schema: "FormEngine",
                newName: "FormFields",
                newSchema: "FE");

            migrationBuilder.RenameTable(
                name: "FormDefinitions",
                schema: "FormEngine",
                newName: "FormDefinitions",
                newSchema: "FE");

            migrationBuilder.Sql(TransferSubmissionTables("FormEngine", "FE"));
        }
    }
}
