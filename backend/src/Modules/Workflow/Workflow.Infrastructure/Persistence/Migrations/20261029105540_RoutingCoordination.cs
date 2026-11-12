using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutingCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_routing_activations",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ForkNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JoinNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkippedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WinnerTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_routing_activations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_routing_results",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_routing_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_routing_scopes",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedNodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_routing_scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_routing_upgrade_snapshots",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DesignerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkspaceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_routing_upgrade_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_activations_OrganizationId_WorkflowInstanceId",
                schema: "Workflow",
                table: "workflow_routing_activations",
                columns: new[] { "OrganizationId", "WorkflowInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_activations_Status_DeadlineAt",
                schema: "Workflow",
                table: "workflow_routing_activations",
                columns: new[] { "Status", "DeadlineAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_results_ActivityInstanceId",
                schema: "Workflow",
                table: "workflow_routing_results",
                column: "ActivityInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_results_OrganizationId_WorkflowInstanceId_ExecutionTokenId",
                schema: "Workflow",
                table: "workflow_routing_results",
                columns: new[] { "OrganizationId", "WorkflowInstanceId", "ExecutionTokenId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_scopes_OrganizationId_WorkflowInstanceId",
                schema: "Workflow",
                table: "workflow_routing_scopes",
                columns: new[] { "OrganizationId", "WorkflowInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_routing_upgrade_snapshots_OrganizationId_WorkflowVersionId",
                schema: "Workflow",
                table: "workflow_routing_upgrade_snapshots",
                columns: new[] { "OrganizationId", "WorkflowVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_routing_activations",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_routing_results",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_routing_scopes",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "workflow_routing_upgrade_snapshots",
                schema: "Workflow");
        }
    }
}
