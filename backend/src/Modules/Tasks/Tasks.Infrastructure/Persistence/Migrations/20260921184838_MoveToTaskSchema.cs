using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves the module from the <c>TK</c> schema to <c>Task</c> and drops the emptied <c>TK</c>. The
    /// migration history table was already carried across by the initializer before this ran (see
    /// <c>MigrationHistorySchemaMove</c>).
    /// </summary>
    public partial class MoveToTaskSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Task");

            migrationBuilder.RenameTable(
                name: "TaskTypes",
                schema: "TK",
                newName: "TaskTypes",
                newSchema: "Task");

            migrationBuilder.RenameTable(
                name: "TaskStatusHistory",
                schema: "TK",
                newName: "TaskStatusHistory",
                newSchema: "Task");

            migrationBuilder.RenameTable(
                name: "Tasks",
                schema: "TK",
                newName: "Tasks",
                newSchema: "Task");

            migrationBuilder.RenameTable(
                name: "TaskAssignments",
                schema: "TK",
                newName: "TaskAssignments",
                newSchema: "Task");

            migrationBuilder.Sql("""
                IF SCHEMA_ID(N'TK') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'TK'))
                    EXEC(N'DROP SCHEMA [TK]');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "TK");

            migrationBuilder.RenameTable(
                name: "TaskTypes",
                schema: "Task",
                newName: "TaskTypes",
                newSchema: "TK");

            migrationBuilder.RenameTable(
                name: "TaskStatusHistory",
                schema: "Task",
                newName: "TaskStatusHistory",
                newSchema: "TK");

            migrationBuilder.RenameTable(
                name: "Tasks",
                schema: "Task",
                newName: "Tasks",
                newSchema: "TK");

            migrationBuilder.RenameTable(
                name: "TaskAssignments",
                schema: "Task",
                newName: "TaskAssignments",
                newSchema: "TK");
        }
    }
}
