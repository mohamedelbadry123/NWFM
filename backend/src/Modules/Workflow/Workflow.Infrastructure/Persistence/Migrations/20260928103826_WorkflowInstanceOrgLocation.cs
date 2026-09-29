using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves an instance's place from the pre-hierarchy <c>GeographyJson</c> blob onto the shared org
    /// hierarchy's levels, as typed columns the instance list can filter by coverage.
    ///
    /// The blob was only ever written by the runtime from <c>WorkflowGeography</c> with web (camelCase)
    /// JSON: <c>clusterCode</c>, <c>regionCode</c> and <c>cityCode</c>. Its region and city were chosen
    /// from, and validated against, Auth's CBU and Branch lookups, so they land in CbuCode and BranchCode.
    /// Anything the backfill cannot read stops the migration rather than being dropped.
    /// </summary>
    public partial class WorkflowInstanceOrgLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in new[] { "ClusterCode", "CbuCode", "BranchCode", "OperationAreaCode" })
            {
                migrationBuilder.AddColumn<string>(
                    name: column,
                    schema: "Workflow",
                    table: "workflow_instances",
                    type: "nvarchar(50)",
                    maxLength: 50,
                    nullable: true);
            }

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Workflow].[workflow_instances]
                           WHERE [GeographyJson] IS NOT NULL AND ISJSON([GeographyJson]) = 0)
                    THROW 50001, 'workflow_instances.GeographyJson holds a value that is not JSON; fix it before moving instances onto the org hierarchy.', 1;

                IF EXISTS (SELECT 1 FROM [Workflow].[workflow_instances]
                           WHERE [GeographyJson] IS NOT NULL
                             AND (LEN(JSON_VALUE([GeographyJson], '$.clusterCode')) > 50
                               OR LEN(COALESCE(JSON_VALUE([GeographyJson], '$.cbuCode'), JSON_VALUE([GeographyJson], '$.regionCode'))) > 50
                               OR LEN(COALESCE(JSON_VALUE([GeographyJson], '$.branchCode'), JSON_VALUE([GeographyJson], '$.cityCode'))) > 50
                               OR LEN(JSON_VALUE([GeographyJson], '$.operationAreaCode')) > 50))
                    THROW 50002, 'workflow_instances.GeographyJson holds an org code longer than 50 characters.', 1;
                """);

            migrationBuilder.Sql("""
                UPDATE [Workflow].[workflow_instances] SET
                    [ClusterCode] = NULLIF(LTRIM(RTRIM(JSON_VALUE([GeographyJson], '$.clusterCode'))), ''),
                    [CbuCode] = NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE([GeographyJson], '$.cbuCode'), JSON_VALUE([GeographyJson], '$.regionCode')))), ''),
                    [BranchCode] = NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE([GeographyJson], '$.branchCode'), JSON_VALUE([GeographyJson], '$.cityCode')))), ''),
                    [OperationAreaCode] = NULLIF(LTRIM(RTRIM(JSON_VALUE([GeographyJson], '$.operationAreaCode'))), '')
                WHERE [GeographyJson] IS NOT NULL;
                """);

            // A non-empty blob that produced no code at all was written in a shape this migration does not
            // know; stop instead of silently turning a placed instance into an unplaced one.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Workflow].[workflow_instances]
                           WHERE [GeographyJson] IS NOT NULL
                             AND LTRIM(RTRIM([GeographyJson])) NOT IN ('null', '{}')
                             AND [ClusterCode] IS NULL AND [CbuCode] IS NULL
                             AND [BranchCode] IS NULL AND [OperationAreaCode] IS NULL)
                    THROW 50003, 'workflow_instances.GeographyJson holds a location in an unrecognised shape; map it by hand before moving instances onto the org hierarchy.', 1;
                """);

            migrationBuilder.DropColumn(
                name: "GeographyJson",
                schema: "Workflow",
                table: "workflow_instances");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_OrganizationId_CbuCode",
                schema: "Workflow",
                table: "workflow_instances",
                columns: new[] { "OrganizationId", "CbuCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflow_instances_OrganizationId_CbuCode",
                schema: "Workflow",
                table: "workflow_instances");

            migrationBuilder.AddColumn<string>(
                name: "GeographyJson",
                schema: "Workflow",
                table: "workflow_instances",
                type: "nvarchar(max)",
                nullable: true);

            // Back to the old shape. An operation area has no place in it and is lost on the way down.
            migrationBuilder.Sql("""
                UPDATE [Workflow].[workflow_instances]
                SET [GeographyJson] = (SELECT [ClusterCode] AS [clusterCode], [CbuCode] AS [regionCode], [BranchCode] AS [cityCode]
                                       FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
                WHERE [ClusterCode] IS NOT NULL OR [CbuCode] IS NOT NULL OR [BranchCode] IS NOT NULL;
                """);

            foreach (var column in new[] { "ClusterCode", "CbuCode", "BranchCode", "OperationAreaCode" })
            {
                migrationBuilder.DropColumn(name: column, schema: "Workflow", table: "workflow_instances");
            }
        }
    }
}
