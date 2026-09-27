using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddC2mClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClosesC2mActivity",
                schema: "Task",
                table: "TaskTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "C2mAttempts",
                schema: "Task",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "C2mLastAttemptAt",
                schema: "Task",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "C2mStatus",
                schema: "Task",
                table: "Tasks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaId",
                schema: "Task",
                table: "Tasks",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WfmTicketId",
                schema: "Task",
                table: "Tasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "C2mActionMappings",
                schema: "Task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FaStatus = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NameEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_C2mActionMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "C2mDispatchLogs",
                schema: "Task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FaId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OpStatus = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_C2mDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_C2mStatus_C2mLastAttemptAt",
                schema: "Task",
                table: "Tasks",
                columns: new[] { "C2mStatus", "C2mLastAttemptAt" },
                filter: "[C2mStatus] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_FaId",
                schema: "Task",
                table: "Tasks",
                column: "FaId",
                filter: "[FaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_C2mActionMappings_ActionCode",
                schema: "Task",
                table: "C2mActionMappings",
                column: "ActionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_C2mDispatchLogs_FaId",
                schema: "Task",
                table: "C2mDispatchLogs",
                column: "FaId");

            migrationBuilder.CreateIndex(
                name: "IX_C2mDispatchLogs_TaskId_AttemptNumber",
                schema: "Task",
                table: "C2mDispatchLogs",
                columns: new[] { "TaskId", "AttemptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "C2mActionMappings",
                schema: "Task");

            migrationBuilder.DropTable(
                name: "C2mDispatchLogs",
                schema: "Task");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_C2mStatus_C2mLastAttemptAt",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_FaId",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ClosesC2mActivity",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "C2mAttempts",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "C2mLastAttemptAt",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "C2mStatus",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "FaId",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WfmTicketId",
                schema: "Task",
                table: "Tasks");
        }
    }
}
