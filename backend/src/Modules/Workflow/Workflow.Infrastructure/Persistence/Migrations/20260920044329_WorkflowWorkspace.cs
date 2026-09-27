using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PinnedChildVersionsJson",
                schema: "Workflow",
                table: "workflow_versions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspaceJson",
                schema: "Workflow",
                table: "workflow_versions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_participants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GeographyJson",
                schema: "Workflow",
                table: "workflow_instances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_instances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_bindings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryResultJson",
                schema: "Workflow",
                table: "integration_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventName",
                schema: "Workflow",
                table: "integration_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventTrigger",
                schema: "Workflow",
                table: "integration_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActivityEvent",
                schema: "Workflow",
                table: "integration_jobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Required",
                schema: "Workflow",
                table: "integration_jobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                schema: "Workflow",
                table: "activity_instances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingOutcome",
                schema: "Workflow",
                table: "activity_instances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                schema: "Workflow",
                table: "activity_instances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaBreachedAt",
                schema: "Workflow",
                table: "activity_instances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workspace_actions",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_actions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_actions_OrganizationId_RequestId",
                schema: "Workflow",
                table: "workspace_actions",
                columns: new[] { "OrganizationId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_actions",
                schema: "Workflow");

            migrationBuilder.DropColumn(
                name: "PinnedChildVersionsJson",
                schema: "Workflow",
                table: "workflow_versions");

            migrationBuilder.DropColumn(
                name: "WorkspaceJson",
                schema: "Workflow",
                table: "workflow_versions");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_participants");

            migrationBuilder.DropColumn(
                name: "GeographyJson",
                schema: "Workflow",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "IsDemo",
                schema: "Workflow",
                table: "workflow_bindings");

            migrationBuilder.DropColumn(
                name: "DeliveryResultJson",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "EventName",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "EventTrigger",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "IsActivityEvent",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "Required",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "DueAt",
                schema: "Workflow",
                table: "activity_instances");

            migrationBuilder.DropColumn(
                name: "PendingOutcome",
                schema: "Workflow",
                table: "activity_instances");

            migrationBuilder.DropColumn(
                name: "Phase",
                schema: "Workflow",
                table: "activity_instances");

            migrationBuilder.DropColumn(
                name: "SlaBreachedAt",
                schema: "Workflow",
                table: "activity_instances");
        }
    }
}
