using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetActivityInstanceId",
                schema: "Workflow",
                table: "workflow_integration_inbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentActivityInstanceId",
                schema: "Workflow",
                table: "workflow_instances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ForkActivityInstanceId",
                schema: "Workflow",
                table: "workflow_execution_tokens",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JoinConsumed",
                schema: "Workflow",
                table: "workflow_execution_tokens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FormDataJson",
                schema: "Workflow",
                table: "work_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionTokenId",
                schema: "Workflow",
                table: "activity_instances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_receipts",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "event_subscriptions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_connections",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Authentication = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProtectedCredentials = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowPrivateNetwork = table.Column<bool>(type: "bit", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    UseTls = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "integration_jobs",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_receipts_OrganizationId_ConnectionId_EventId",
                schema: "Workflow",
                table: "event_receipts",
                columns: new[] { "OrganizationId", "ConnectionId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_receipts_OrganizationId_ConnectionId_EventKey_CorrelationId",
                schema: "Workflow",
                table: "event_receipts",
                columns: new[] { "OrganizationId", "ConnectionId", "EventKey", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_event_subscriptions_ActivityInstanceId",
                schema: "Workflow",
                table: "event_subscriptions",
                column: "ActivityInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_subscriptions_OrganizationId_ConnectionId_EventKey_CorrelationId",
                schema: "Workflow",
                table: "event_subscriptions",
                columns: new[] { "OrganizationId", "ConnectionId", "EventKey", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_OrganizationId_Name",
                schema: "Workflow",
                table: "integration_connections",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_jobs_ActivityInstanceId",
                schema: "Workflow",
                table: "integration_jobs",
                column: "ActivityInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_jobs_OrganizationId_OperationKey",
                schema: "Workflow",
                table: "integration_jobs",
                columns: new[] { "OrganizationId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_jobs_Status_NextAttemptAt",
                schema: "Workflow",
                table: "integration_jobs",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_receipts",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "event_subscriptions",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "integration_connections",
                schema: "Workflow");

            migrationBuilder.DropTable(
                name: "integration_jobs",
                schema: "Workflow");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "Workflow",
                table: "workflow_integration_inbox");

            migrationBuilder.DropColumn(
                name: "TargetActivityInstanceId",
                schema: "Workflow",
                table: "workflow_integration_inbox");

            migrationBuilder.DropColumn(
                name: "ParentActivityInstanceId",
                schema: "Workflow",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "ForkActivityInstanceId",
                schema: "Workflow",
                table: "workflow_execution_tokens");

            migrationBuilder.DropColumn(
                name: "JoinConsumed",
                schema: "Workflow",
                table: "workflow_execution_tokens");

            migrationBuilder.DropColumn(
                name: "FormDataJson",
                schema: "Workflow",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "ExecutionTokenId",
                schema: "Workflow",
                table: "activity_instances");
        }
    }
}
