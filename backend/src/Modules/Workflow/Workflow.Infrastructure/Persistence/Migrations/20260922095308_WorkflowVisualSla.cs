using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowVisualSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                schema: "Workflow",
                table: "sla_policies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldActivityCode",
                schema: "Workflow",
                table: "sla_policies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventNodeKey",
                schema: "Workflow",
                table: "integration_jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSlaAlertAt",
                schema: "Workflow",
                table: "activity_instances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaAlertCount",
                schema: "Workflow",
                table: "activity_instances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_sla_policies_OrganizationId_DepartmentCode_FieldActivityCode",
                schema: "Workflow",
                table: "sla_policies",
                columns: new[] { "OrganizationId", "DepartmentCode", "FieldActivityCode" },
                unique: true,
                filter: "[IsActive] = 1 AND [DepartmentCode] IS NOT NULL AND [FieldActivityCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sla_policies_OrganizationId_DepartmentCode_FieldActivityCode",
                schema: "Workflow",
                table: "sla_policies");

            migrationBuilder.DropColumn(
                name: "DepartmentCode",
                schema: "Workflow",
                table: "sla_policies");

            migrationBuilder.DropColumn(
                name: "FieldActivityCode",
                schema: "Workflow",
                table: "sla_policies");

            migrationBuilder.DropColumn(
                name: "EventNodeKey",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "NextSlaAlertAt",
                schema: "Workflow",
                table: "activity_instances");

            migrationBuilder.DropColumn(
                name: "SlaAlertCount",
                schema: "Workflow",
                table: "activity_instances");
        }
    }
}
