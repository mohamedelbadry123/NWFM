using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowReceiptKeyReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowReceiptKeys",
                schema: "FormEngine",
                columns: table => new
                {
                    ClientSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowReceiptKeys", x => x.ClientSubmissionId);
                });
            // Metadata-only, once at upgrade. Runtime lookups are primary-key seeks.
            migrationBuilder.Sql("""
                DECLARE @table sysname, @sql nvarchar(max);
                DECLARE receipts CURSOR LOCAL FAST_FORWARD FOR
                    SELECT DISTINCT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
                    JOIN [FormEngine].[FormDefinitions] f ON f.SubmissionTable=t.name
                    WHERE s.name=N'FormEngine' AND t.name LIKE N'SUB[_]%';
                OPEN receipts;
                FETCH NEXT FROM receipts INTO @table;
                WHILE @@FETCH_STATUS=0
                BEGIN
                    SET @sql=N'INSERT [FormEngine].[WorkflowReceiptKeys] (ClientSubmissionId)
                        SELECT DISTINCT r.ClientSubmissionId FROM [FormEngine].'+QUOTENAME(@table)+N' r
                        WHERE r.ContextType=N''WorkItem'' AND r.ClientSubmissionId IS NOT NULL
                        AND NOT EXISTS (SELECT 1 FROM [FormEngine].[WorkflowReceiptKeys] k WHERE k.ClientSubmissionId=r.ClientSubmissionId);';
                    EXEC sys.sp_executesql @sql;
                    FETCH NEXT FROM receipts INTO @table;
                END
                CLOSE receipts;
                DEALLOCATE receipts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowReceiptKeys",
                schema: "FormEngine");
        }
    }
}
