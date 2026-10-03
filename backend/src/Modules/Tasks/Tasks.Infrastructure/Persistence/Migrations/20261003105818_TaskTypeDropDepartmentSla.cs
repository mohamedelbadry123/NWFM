using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskTypeDropDepartmentSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosesC2mActivity",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "CompletionSlaHours",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "DepartmentCode",
                schema: "Task",
                table: "TaskTypes");

            migrationBuilder.DropColumn(
                name: "FillSlaHours",
                schema: "Task",
                table: "TaskTypes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClosesC2mActivity",
                schema: "Task",
                table: "TaskTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CompletionSlaHours",
                schema: "Task",
                table: "TaskTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                schema: "Task",
                table: "TaskTypes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FillSlaHours",
                schema: "Task",
                table: "TaskTypes",
                type: "int",
                nullable: true);
        }
    }
}
