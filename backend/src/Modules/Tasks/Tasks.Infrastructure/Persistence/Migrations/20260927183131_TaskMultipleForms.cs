using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A task type lists several forms and a task pins several. Each type's one form becomes its first
    /// listed form, and each task's one pinned form becomes its first pinned form, carrying the fill
    /// counters it had — then the single-form columns go. Ordered by hand: the tables are created and
    /// filled before the columns they are filled from are dropped.
    /// </summary>
    public partial class TaskMultipleForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskForms",
                schema: "Task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormVersionNo = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsC2mClosingForm = table.Column<bool>(type: "bit", nullable: false),
                    SubmissionCount = table.Column<int>(type: "int", nullable: false),
                    LastSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFilledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AddedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskForms_Tasks_FieldTaskId",
                        column: x => x.FieldTaskId,
                        principalSchema: "Task",
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskTypeForms",
                schema: "Task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsC2mClosingForm = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypeForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTypeForms_TaskTypes_TaskTypeId",
                        column: x => x.TaskTypeId,
                        principalSchema: "Task",
                        principalTable: "TaskTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskForms_FieldTaskId_FormDefinitionId",
                schema: "Task",
                table: "TaskForms",
                columns: new[] { "FieldTaskId", "FormDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskForms_FormDefinitionId",
                schema: "Task",
                table: "TaskForms",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypeForms_FormDefinitionId",
                schema: "Task",
                table: "TaskTypeForms",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypeForms_TaskTypeId_FormDefinitionId",
                schema: "Task",
                table: "TaskTypeForms",
                columns: new[] { "TaskTypeId", "FormDefinitionId" },
                unique: true);

            // Each type's form is its first and — having only one — its closing form.
            migrationBuilder.Sql(@"
INSERT INTO [Task].[TaskTypeForms] ([Id], [TaskTypeId], [FormDefinitionId], [SortOrder], [IsC2mClosingForm], [CreatedAt], [UpdatedAt])
SELECT NEWID(), tt.[Id], tt.[FormDefinitionId], 0, CAST(1 AS bit), tt.[CreatedAt], tt.[UpdatedAt]
FROM [Task].[TaskTypes] tt;");

            // Each task's pinned form, with the fills it has had: the task-wide counters were its alone.
            migrationBuilder.Sql(@"
INSERT INTO [Task].[TaskForms] ([Id], [FieldTaskId], [FormDefinitionId], [FormVersionNo], [SortOrder], [Source], [IsRequired],
    [IsC2mClosingForm], [SubmissionCount], [LastSubmissionId], [SubmittedDate], [LastFilledBy], [AddedBy], [CreatedAt], [UpdatedAt])
SELECT NEWID(), t.[Id], t.[FormDefinitionId], t.[FormVersionNo], 0, N'TYPE', CAST(1 AS bit),
    CAST(1 AS bit), t.[SubmissionCount], t.[LastSubmissionId], t.[SubmittedDate], t.[LastFilledBy], t.[CreatedBy], t.[CreatedAt], t.[UpdatedAt]
FROM [Task].[Tasks] t;");

            migrationBuilder.DropIndex(
                name: "IX_TaskTypes_FormDefinitionId",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_FormDefinitionId",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "FormDefinitionId",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "FormDefinitionId",
                schema: "Task",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "FormVersionNo",
                schema: "Task",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FormDefinitionId",
                schema: "Task",
                table: "TaskTypes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "FormDefinitionId",
                schema: "Task",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "FormVersionNo",
                schema: "Task",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Back to one form each: the first. Forms added to single tasks, and a type's later forms, are lost.
            migrationBuilder.Sql(@"
UPDATE tt SET tt.[FormDefinitionId] = f.[FormDefinitionId]
FROM [Task].[TaskTypes] tt
CROSS APPLY (SELECT TOP (1) x.[FormDefinitionId] FROM [Task].[TaskTypeForms] x WHERE x.[TaskTypeId] = tt.[Id] ORDER BY x.[SortOrder]) f;");

            migrationBuilder.Sql(@"
UPDATE t SET t.[FormDefinitionId] = f.[FormDefinitionId], t.[FormVersionNo] = f.[FormVersionNo]
FROM [Task].[Tasks] t
CROSS APPLY (SELECT TOP (1) x.[FormDefinitionId], x.[FormVersionNo] FROM [Task].[TaskForms] x WHERE x.[FieldTaskId] = t.[Id] ORDER BY x.[SortOrder]) f;");

            migrationBuilder.DropTable(
                name: "TaskForms",
                schema: "Task");

            migrationBuilder.DropTable(
                name: "TaskTypeForms",
                schema: "Task");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_FormDefinitionId",
                schema: "Task",
                table: "TaskTypes",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_FormDefinitionId",
                schema: "Task",
                table: "Tasks",
                column: "FormDefinitionId");
        }
    }
}
