using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskComputedValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskComputedValues",
                schema: "Task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutputType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ValueText = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ValueNumber = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FormVersionNo = table.Column<int>(type: "int", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskComputedValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskComputedValues_TaskForms_TaskFormId",
                        column: x => x.TaskFormId,
                        principalSchema: "Task",
                        principalTable: "TaskForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskComputedValues_FieldTaskId",
                schema: "Task",
                table: "TaskComputedValues",
                column: "FieldTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComputedValues_FormDefinitionId_Key",
                schema: "Task",
                table: "TaskComputedValues",
                columns: new[] { "FormDefinitionId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskComputedValues_TaskFormId_Key",
                schema: "Task",
                table: "TaskComputedValues",
                columns: new[] { "TaskFormId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskComputedValues",
                schema: "Task");
        }
    }
}
