using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowFormReceiptOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletedFormReceiptKey",
                schema: "Workflow",
                table: "work_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormReceiptAttempt",
                schema: "Workflow",
                table: "work_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "form_receipt_reservations",
                schema: "Workflow",
                columns: table => new
                {
                    ReceiptKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_receipt_reservations", x => x.ReceiptKey);
                });

            // One-time legacy backfill. Runtime submissions use primary-key seeks, never JSON scans.
            migrationBuilder.Sql("""
                WITH receipts AS (
                    SELECT Id AS ReceiptKey, OrganizationId, Id AS WorkItemId FROM [Workflow].[work_items]
                    UNION
                    SELECT TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(FormProgressJson)=1 THEN FormProgressJson ELSE N'{}' END, '$.VisitId')), OrganizationId, Id FROM [Workflow].[work_items]
                    UNION
                    SELECT TRY_CONVERT(uniqueidentifier, JSON_VALUE(v.value, '$.VisitId')), w.OrganizationId, w.Id
                    FROM [Workflow].[work_items] w CROSS APPLY OPENJSON(CASE WHEN ISJSON(w.FormProgressJson)=1 THEN w.FormProgressJson ELSE N'{}' END, '$.Visits') v
                    UNION
                    SELECT i.FormVisitId, w.OrganizationId, w.Id FROM [Workflow].[mapping_invocations] i
                    JOIN [Workflow].[work_items] w ON w.ActivityInstanceId=i.OriginActivityId
                    WHERE i.ExecutionKind=N'Step'
                )
                INSERT [Workflow].[form_receipt_reservations] (ReceiptKey, OrganizationId, WorkItemId)
                SELECT ReceiptKey, OrganizationId, WorkItemId FROM receipts WHERE ReceiptKey IS NOT NULL;
                UPDATE [Workflow].[work_items] SET FormReceiptAttempt=NEWID()
                WHERE ClaimedByUserId IS NULL AND Status NOT IN (N'Completed',N'Cancelled');
                """);
            migrationBuilder.CreateIndex(
                name: "IX_form_receipt_reservations_WorkItemId",
                schema: "Workflow",
                table: "form_receipt_reservations",
                column: "WorkItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Attempt entropy and completion pointers are operational references, not disposable
            // metadata. Even an unclaimed attempt may own a committed orphan FormEngine receipt.
            // Fail before any DDL; only legacy-only databases can safely cross this rollback boundary.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Workflow].[work_items]
                    WHERE FormReceiptAttempt IS NOT NULL OR CompletedFormReceiptKey IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [Workflow].[work_items] w
                        CROSS APPLY OPENJSON(CASE WHEN ISJSON(w.FormProgressJson)=1 THEN w.FormProgressJson ELSE N'{}' END, '$.Visits') v
                        WHERE JSON_VALUE(v.value, '$.ReceiptKey') IS NOT NULL)
                    THROW 51041, 'Workflow receipt ownership cannot be downgraded while form attempts or receipt references exist. Preserve this migration and use a forward-compatible deployment.', 1;
                """);
            migrationBuilder.DropTable(
                name: "form_receipt_reservations",
                schema: "Workflow");

            migrationBuilder.DropColumn(
                name: "CompletedFormReceiptKey",
                schema: "Workflow",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "FormReceiptAttempt",
                schema: "Workflow",
                table: "work_items");
        }
    }
}
