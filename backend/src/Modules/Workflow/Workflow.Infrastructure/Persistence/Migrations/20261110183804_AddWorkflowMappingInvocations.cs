using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowMappingInvocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionKind",
                schema: "Workflow",
                table: "integration_jobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<Guid>(
                name: "InvocationId",
                schema: "Workflow",
                table: "integration_jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mapping_invocations",
                schema: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetNodeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExecutionKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ParentInvocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvocationInputsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WindowAnchor = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparedRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StepValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FormVisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Applied = table.Column<bool>(type: "bit", nullable: false),
                    GraphAdvanced = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutOutcome = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mapping_invocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_jobs_InvocationId",
                schema: "Workflow",
                table: "integration_jobs",
                column: "InvocationId",
                unique: true,
                filter: "[InvocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_mapping_invocations_OrganizationId_OperationKey",
                schema: "Workflow",
                table: "mapping_invocations",
                columns: new[] { "OrganizationId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mapping_invocations_OriginActivityId_FormVisitId",
                schema: "Workflow",
                table: "mapping_invocations",
                columns: new[] { "OriginActivityId", "FormVisitId" });

            migrationBuilder.CreateIndex(
                name: "IX_mapping_invocations_ParentInvocationId_ActionId",
                schema: "Workflow",
                table: "mapping_invocations",
                columns: new[] { "ParentInvocationId", "ActionId" },
                unique: true,
                filter: "[ParentInvocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_mapping_invocations_RootInstanceId_TargetInstanceId",
                schema: "Workflow",
                table: "mapping_invocations",
                columns: new[] { "RootInstanceId", "TargetInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_mapping_invocations_Status_Deadline",
                schema: "Workflow",
                table: "mapping_invocations",
                columns: new[] { "Status", "Deadline" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mapping_invocations",
                schema: "Workflow");

            migrationBuilder.DropIndex(
                name: "IX_integration_jobs_InvocationId",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "ExecutionKind",
                schema: "Workflow",
                table: "integration_jobs");

            migrationBuilder.DropColumn(
                name: "InvocationId",
                schema: "Workflow",
                table: "integration_jobs");
        }
    }
}
