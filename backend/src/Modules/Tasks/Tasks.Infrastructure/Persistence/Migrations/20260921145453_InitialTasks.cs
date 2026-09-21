using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "TK");

            migrationBuilder.CreateTable(
                name: "TaskTypes",
                schema: "TK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FillSlaHours = table.Column<int>(type: "int", nullable: true),
                    CompletionSlaHours = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                schema: "TK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TaskTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormVersionNo = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AdditionalDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CbuCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BranchCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OperationAreaCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletionDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FillSlaHours = table.Column<int>(type: "int", nullable: true),
                    CompletionSlaHours = table.Column<int>(type: "int", nullable: true),
                    AssignedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFilledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SubmissionCount = table.Column<int>(type: "int", nullable: false),
                    LastSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReturnedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnCount = table.Column<int>(type: "int", nullable: false),
                    ExpiredBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_TaskTypes_TaskTypeId",
                        column: x => x.TaskTypeId,
                        principalSchema: "TK",
                        principalTable: "TaskTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignments",
                schema: "TK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignments_Tasks_FieldTaskId",
                        column: x => x.FieldTaskId,
                        principalSchema: "TK",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskStatusHistory",
                schema: "TK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskStatusHistory_Tasks_FieldTaskId",
                        column: x => x.FieldTaskId,
                        principalSchema: "TK",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_FieldTaskId",
                schema: "TK",
                table: "TaskAssignments",
                column: "FieldTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_TeamId_Status",
                schema: "TK",
                table: "TaskAssignments",
                columns: new[] { "TeamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_BranchCode_Status",
                schema: "TK",
                table: "Tasks",
                columns: new[] { "BranchCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CbuCode_Status",
                schema: "TK",
                table: "Tasks",
                columns: new[] { "CbuCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CreatedAt",
                schema: "TK",
                table: "Tasks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DepartmentCode",
                schema: "TK",
                table: "Tasks",
                column: "DepartmentCode");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DueDate",
                schema: "TK",
                table: "Tasks",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_FormDefinitionId",
                schema: "TK",
                table: "Tasks",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OperationAreaCode",
                schema: "TK",
                table: "Tasks",
                column: "OperationAreaCode");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status",
                schema: "TK",
                table: "Tasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TaskNumber",
                schema: "TK",
                table: "Tasks",
                column: "TaskNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TaskTypeId",
                schema: "TK",
                table: "Tasks",
                column: "TaskTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskStatusHistory_FieldTaskId_ChangedDate",
                schema: "TK",
                table: "TaskStatusHistory",
                columns: new[] { "FieldTaskId", "ChangedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_Code",
                schema: "TK",
                table: "TaskTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_FormDefinitionId",
                schema: "TK",
                table: "TaskTypes",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_IsActive",
                schema: "TK",
                table: "TaskTypes",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskAssignments",
                schema: "TK");

            migrationBuilder.DropTable(
                name: "TaskStatusHistory",
                schema: "TK");

            migrationBuilder.DropTable(
                name: "Tasks",
                schema: "TK");

            migrationBuilder.DropTable(
                name: "TaskTypes",
                schema: "TK");
        }
    }
}
